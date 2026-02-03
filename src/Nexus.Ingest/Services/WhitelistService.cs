using Azure;
using Azure.Data.Tables;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Services;

public sealed class WhitelistService
{
    private readonly TableClient _whitelistTable;
    private readonly TableClient _pendingTable;
    private readonly TableClient _itemsTable;
    private const string PartitionKey = "domain";

    public WhitelistService(TableServiceClient tableService)
    {
        _whitelistTable = tableService.GetTableClient("WhitelistedDomains");
        _pendingTable = tableService.GetTableClient("PendingEmails");
        _itemsTable = tableService.GetTableClient("Items");
    }

    public async Task<bool> IsDomainWhitelisted(string domain, CancellationToken ct)
    {
        try
        {
            await _whitelistTable.GetEntityAsync<TableEntity>(
                PartitionKey, domain.ToLowerInvariant(), cancellationToken: ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    /// <summary>
    /// Adds domains if not already whitelisted. Returns list of newly added domains.
    /// </summary>
    public async Task<List<string>> AddDomainsIfNew(
        List<string> domains, string addedBy, CancellationToken ct)
    {
        var newlyAdded = new List<string>();

        foreach (var domain in domains)
        {
            var normalized = domain.ToLowerInvariant();
            if (await IsDomainWhitelisted(normalized, ct))
                continue;

            var entity = new TableEntity(PartitionKey, normalized)
            {
                { "AddedAt", DateTimeOffset.UtcNow },
                { "AddedBy", addedBy },
                { "EmailCount", 0 }
            };
            await _whitelistTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
            newlyAdded.Add(normalized);
        }

        return newlyAdded;
    }

    public async Task AddDomains(List<string> domains, string addedBy, CancellationToken ct)
    {
        foreach (var domain in domains)
        {
            var normalized = domain.ToLowerInvariant();
            var entity = new TableEntity(PartitionKey, normalized)
            {
                { "AddedAt", DateTimeOffset.UtcNow },
                { "AddedBy", addedBy },
                { "EmailCount", 0 }
            };
            await _whitelistTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }
    }

    public async Task IncrementEmailCount(string domain, CancellationToken ct)
    {
        try
        {
            var response = await _whitelistTable.GetEntityAsync<TableEntity>(
                PartitionKey, domain.ToLowerInvariant(), cancellationToken: ct);
            var entity = response.Value;
            var count = entity.GetInt32("EmailCount") ?? 0;
            entity["EmailCount"] = count + 1;
            await _whitelistTable.UpdateEntityAsync(entity, entity.ETag, cancellationToken: ct);
        }
        catch (RequestFailedException)
        {
            // Ignore if domain not found
        }
    }

    public async Task<List<WhitelistedDomainDto>> ListAll(CancellationToken ct)
    {
        var results = new List<WhitelistedDomainDto>();
        await foreach (var entity in _whitelistTable.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{PartitionKey}'", cancellationToken: ct))
        {
            results.Add(new WhitelistedDomainDto
            {
                Domain = entity.RowKey!,
                AddedAt = entity.GetDateTimeOffset("AddedAt") ?? DateTimeOffset.MinValue,
                AddedBy = entity.GetString("AddedBy") ?? "unknown",
                EmailCount = entity.GetInt32("EmailCount") ?? 0
            });
        }
        return results;
    }

    public async Task RemoveDomain(string domain, CancellationToken ct)
    {
        try
        {
            await _whitelistTable.DeleteEntityAsync(
                PartitionKey, domain.ToLowerInvariant(), cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone — idempotent
        }
    }

    /// <summary>
    /// Move all PendingEmails from a newly whitelisted domain to the Items table.
    /// </summary>
    public async Task PromotePendingEmails(string domain, CancellationToken ct)
    {
        var pending = _pendingTable.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{domain}'", cancellationToken: ct);

        await foreach (var entity in pending)
        {
            // Re-create in Items with PartitionKey = "email"
            var itemEntity = new TableEntity("email", entity.RowKey!);
            foreach (var prop in entity)
            {
                if (prop.Key is "PartitionKey" or "RowKey" or "odata.etag" or "Timestamp")
                    continue;
                itemEntity[prop.Key] = prop.Value;
            }

            await _itemsTable.UpsertEntityAsync(itemEntity, TableUpdateMode.Replace, ct);
            await _pendingTable.DeleteEntityAsync(domain, entity.RowKey!, cancellationToken: ct);
        }
    }
}
