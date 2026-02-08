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

    public async Task Process(Message message, string changeType, string agentName, CancellationToken ct)
    {
        var direction = DetermineDirection(message);
        var senderDomain = ExtractDomain(message.From?.EmailAddress?.Address);

        _logger.LogInformation(
            "Processing {Direction} email for {Agent}: {Subject} from {From}",
            direction, agentName, message.Subject, message.From?.EmailAddress?.Address);

        // Outbound: auto-whitelist TO + CC recipients by full email address (use BCC to avoid; not domain)
        if (direction == "outbound")
        {
            var recipientEmails = (message.ToRecipients ?? [])
                .Concat(message.CcRecipients ?? [])
                .Where(r => !string.IsNullOrEmpty(r.EmailAddress?.Address))
                .Select(r => r.EmailAddress!.Address!.ToLowerInvariant())
                .Distinct()
                .ToList();

            var newEmails = await _whitelist.AddEmailsIfNew(recipientEmails, "auto-email", ct);

            foreach (var email in newEmails)
            {
                _logger.LogInformation("Auto-whitelisted email from outbound: {Email}", email);
                await _whitelist.PromotePendingByEmail(email, ct);
            }
        }

        // Build table entity with agent routing info
        var entity = await MapEmailToEntity(message, direction, agentName, ct);

        // Inbound: check whitelist (full email OR domain)
        var senderEmail = (message.From?.EmailAddress?.Address ?? "").ToLowerInvariant();
        if (direction == "inbound")
        {
            if (!string.IsNullOrEmpty(senderEmail) &&
                await _whitelist.IsSenderWhitelisted(senderEmail, senderDomain, ct))
            {
                await _itemsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
                await _whitelist.IncrementEmailCount(senderEmail, senderDomain, ct);
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
        Message message, string direction, string agentName, CancellationToken ct)
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
            // Agent routing info
            { "AgentName", agentName },
            { "SourceType", "graph-email" },
            // Standard fields
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
            { "IngestedAt", DateTimeOffset.UtcNow }
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
