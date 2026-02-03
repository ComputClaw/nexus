using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using Nexus.Ingest.Helpers;

namespace Nexus.Ingest.Services;

/// <summary>
/// Processes email messages: applies whitelist logic, routes to Items or PendingEmails table.
/// </summary>
public sealed class EmailIngestionService
{
    private readonly TableClient _itemsTable;
    private readonly TableClient _pendingTable;
    private readonly WhitelistService _whitelist;
    private readonly BlobStorageService _blobService;
    private readonly string _userEmail;
    private readonly ILogger<EmailIngestionService> _logger;

    public EmailIngestionService(
        TableServiceClient tableService,
        WhitelistService whitelist,
        BlobStorageService blobService,
        IConfiguration config,
        ILogger<EmailIngestionService> logger)
    {
        _itemsTable = tableService.GetTableClient("Items");
        _pendingTable = tableService.GetTableClient("PendingEmails");
        _whitelist = whitelist;
        _blobService = blobService;
        _userEmail = (config["Graph:UserId"] ?? "").ToLowerInvariant();
        _logger = logger;
    }

    public async Task Process(Message message, string changeType, CancellationToken ct)
    {
        var direction = DetermineDirection(message);
        var senderDomain = ExtractDomain(message.From?.EmailAddress?.Address);

        _logger.LogInformation(
            "Processing {Direction} email: {Subject} from {From}",
            direction, message.Subject, message.From?.EmailAddress?.Address);

        // Outbound: auto-whitelist TO recipients (not CC)
        if (direction == "outbound")
        {
            var toRecipients = (message.ToRecipients ?? [])
                .Where(r => !string.IsNullOrEmpty(r.EmailAddress?.Address))
                .Select(r => ExtractDomain(r.EmailAddress!.Address!))
                .Distinct()
                .ToList();

            var newDomains = await _whitelist.AddDomainsIfNew(toRecipients, "auto-email", ct);

            foreach (var domain in newDomains)
            {
                _logger.LogInformation("Auto-whitelisted domain from outbound email: {Domain}", domain);
                await _whitelist.PromotePendingEmails(domain, ct);
            }
        }

        // Build table entity
        var entity = await MapEmailToEntity(message, direction, ct);

        // Inbound: check whitelist
        if (direction == "inbound")
        {
            if (!string.IsNullOrEmpty(senderDomain) &&
                await _whitelist.IsDomainWhitelisted(senderDomain, ct))
            {
                await _itemsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
                await _whitelist.IncrementEmailCount(senderDomain, ct);
                _logger.LogInformation("Stored whitelisted email in Items: {Subject}", message.Subject);
            }
            else
            {
                // Park in PendingEmails with PartitionKey = sender domain
                entity["PartitionKey"] = senderDomain ?? "unknown";
                await _pendingTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
                _logger.LogInformation(
                    "Parked non-whitelisted email in PendingEmails: {Subject} (domain: {Domain})",
                    message.Subject, senderDomain);
            }
        }
        else
        {
            // Outbound always goes to Items
            await _itemsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
            _logger.LogInformation("Stored outbound email in Items: {Subject}", message.Subject);
        }
    }

    private string DetermineDirection(Message message)
    {
        var fromAddress = message.From?.EmailAddress?.Address?.ToLowerInvariant();
        return fromAddress == _userEmail ? "outbound" : "inbound";
    }

    private async Task<TableEntity> MapEmailToEntity(
        Message message, string direction, CancellationToken ct)
    {
        var receivedAt = message.ReceivedDateTime ?? message.SentDateTime ?? DateTimeOffset.UtcNow;
        var sourceId = SanitizeRowKey(message.Id ?? "unknown");
        var fileName = FileNameBuilder.ForEmail(message);

        // Store full body in Blob Storage
        var fullBodyBlobPath = await _blobService.StoreEmailBody(message, ct);

        var toAddresses = (message.ToRecipients ?? [])
            .Select(r => r.EmailAddress?.Address)
            .Where(a => a != null);
        var ccAddresses = (message.CcRecipients ?? [])
            .Select(r => r.EmailAddress?.Address)
            .Where(a => a != null);

        return new TableEntity("email", sourceId)
        {
            { "FileName", fileName },
            { "Action", "created" },
            { "Source", "graph-mail" },
            { "SourceId", message.Id },
            { "Direction", direction },
            { "ConversationId", message.ConversationId },
            { "Subject", message.Subject },
            { "From", message.From?.EmailAddress?.Address },
            { "To", JsonSerializer.Serialize(toAddresses) },
            { "Cc", JsonSerializer.Serialize(ccAddresses) },
            { "BodyText", HtmlStripper.StripHtml(message.UniqueBody?.Content) },
            { "FullBodyBlob", fullBodyBlobPath },
            { "ReceivedAt", receivedAt },
            { "IngestedAt", DateTimeOffset.UtcNow },
            { "SyncStatus", "pending" }
        };
    }

    private static string ExtractDomain(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "unknown";
        var at = email.IndexOf('@');
        return at >= 0 ? email[(at + 1)..].ToLowerInvariant() : "unknown";
    }

    private static string SanitizeRowKey(string id)
    {
        return id
            .Replace("/", "")
            .Replace("\\", "")
            .Replace("#", "")
            .Replace("?", "");
    }
}
