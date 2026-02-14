using Azure;
using Azure.Data.Tables;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Whitelist;

public sealed class WhitelistService
{
    private readonly TableClient _whitelistTable;
    private readonly TableClient _pendingTable;
    private readonly TableClient _itemsTable;
    private const string DomainPartition = "domain";
    private const string EmailPartition = "email";

    public WhitelistService(TableServiceClient tableService)
    {
        _whitelistTable = tableService.GetTableClient("Whitelist");
        _pendingTable = tableService.GetTableClient("PendingEmails");
        _itemsTable = tableService.GetTableClient("Items");
    }

    /// <summary>
    /// Check if a sender is whitelisted — by full email OR by domain.
    /// </summary>
    public async Task<bool> IsSenderWhitelisted(string email, string domain, CancellationToken ct)
    {
        // Check full email first (faster, more specific)
        if (await IsEmailWhitelisted(email, ct))
            return true;
        // Fall back to domain check
        return await IsDomainWhitelisted(domain, ct);
    }

    public async Task<bool> IsDomainWhitelisted(string domain, CancellationToken ct)
    {
        try
        {
            await _whitelistTable.GetEntityAsync<TableEntity>(
                DomainPartition, domain.ToLowerInvariant(), cancellationToken: ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<bool> IsEmailWhitelisted(string email, CancellationToken ct)
    {
        try
        {
            await _whitelistTable.GetEntityAsync<TableEntity>(
                EmailPartition, email.ToLowerInvariant(), cancellationToken: ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    /// <summary>
    /// Auto-whitelist full email addresses (from outbound recipients).
    /// Returns list of newly added emails.
    /// </summary>
    public async Task<List<string>> AddEmailsIfNew(
        List<string> emails, string addedBy, CancellationToken ct)
    {
        var newlyAdded = new List<string>();

        foreach (var email in emails)
        {
            var normalized = email.ToLowerInvariant();
            if (await IsEmailWhitelisted(normalized, ct))
                continue;

            var entity = new TableEntity(EmailPartition, normalized)
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

    /// <summary>
    /// Adds domains if not already whitelisted. Returns list of newly added domains.
    /// Used by manual whitelist API and calendar/meeting auto-whitelist.
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

            var entity = new TableEntity(DomainPartition, normalized)
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
            var entity = new TableEntity(DomainPartition, normalized)
            {
                { "AddedAt", DateTimeOffset.UtcNow },
                { "AddedBy", addedBy },
                { "EmailCount", 0 }
            };
            await _whitelistTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }
    }

    public async Task IncrementEmailCount(string senderEmail, string senderDomain, CancellationToken ct)
    {
        // Try incrementing on email entry first, then domain
        if (!await TryIncrementCount(EmailPartition, senderEmail.ToLowerInvariant(), ct))
            await TryIncrementCount(DomainPartition, senderDomain.ToLowerInvariant(), ct);
    }

    private async Task<bool> TryIncrementCount(string partitionKey, string rowKey, CancellationToken ct)
    {
        try
        {
            var response = await _whitelistTable.GetEntityAsync<TableEntity>(
                partitionKey, rowKey, cancellationToken: ct);
            var entity = response.Value;
            var count = entity.GetInt32("EmailCount") ?? 0;
            entity["EmailCount"] = count + 1;
            await _whitelistTable.UpdateEntityAsync(entity, entity.ETag, cancellationToken: ct);
            return true;
        }
        catch (RequestFailedException)
        {
            return false;
        }
    }

    public async Task<List<WhitelistedDomainDto>> ListAll(CancellationToken ct)
    {
        var results = new List<WhitelistedDomainDto>();

        // List both domain and email entries
        await foreach (var entity in _whitelistTable.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{DomainPartition}' or PartitionKey eq '{EmailPartition}'",
            cancellationToken: ct))
        {
            results.Add(new WhitelistedDomainDto
            {
                Domain = entity.RowKey!,
                AddedAt = entity.GetDateTimeOffset("AddedAt") ?? DateTimeOffset.MinValue,
                AddedBy = entity.GetString("AddedBy") ?? "unknown",
                EmailCount = entity.GetInt32("EmailCount") ?? 0,
                Type = entity.PartitionKey!
            });
        }
        return results;
    }

    public async Task RemoveDomain(string domain, CancellationToken ct)
    {
        try
        {
            await _whitelistTable.DeleteEntityAsync(
                DomainPartition, domain.ToLowerInvariant(), cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone — idempotent
        }
    }

    public async Task RemoveEmail(string email, CancellationToken ct)
    {
        try
        {
            await _whitelistTable.DeleteEntityAsync(
                EmailPartition, email.ToLowerInvariant(), cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone — idempotent
        }
    }

    /// <summary>
    /// Move PendingEmails from a newly whitelisted domain to the Items table.
    /// </summary>
    public async Task PromotePendingByDomain(string domain, CancellationToken ct)
    {
        await PromotePendingWhere(
            $"PartitionKey eq '{domain}'", domain, filterBySender: null, ct);
    }

    /// <summary>
    /// Move PendingEmails from a newly whitelisted email address to the Items table.
    /// Queries by domain partition, filters by From field.
    /// </summary>
    public async Task PromotePendingByEmail(string email, CancellationToken ct)
    {
        var domain = ExtractDomain(email);
        await PromotePendingWhere(
            $"PartitionKey eq '{domain}'", domain, filterBySender: email.ToLowerInvariant(), ct);
    }

    private async Task PromotePendingWhere(
        string filter, string domain, string? filterBySender, CancellationToken ct)
    {
        var pending = _pendingTable.QueryAsync<TableEntity>(filter: filter, cancellationToken: ct);

        await foreach (var entity in pending)
        {
            // If filtering by sender, skip non-matching emails
            if (filterBySender != null)
            {
                var from = entity.GetString("From")?.ToLowerInvariant();
                if (from != filterBySender)
                    continue;
            }

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

    private static string ExtractDomain(string email)
    {
        var at = email.IndexOf('@');
        return at >= 0 ? email[(at + 1)..].ToLowerInvariant() : "unknown";
    }
}
