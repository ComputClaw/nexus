using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;
using Nexus.Ingest.Webhooks.Processors;
using Nexus.Ingest.Whitelist;

namespace Nexus.Ingest.Webhooks;

/// <summary>
/// Queue-triggered function that dispatches webhook messages to source-specific processors
/// and stores the resulting items via IngestionService.
/// </summary>
public sealed class WebhookProcessorFunction
{
    private readonly IReadOnlyDictionary<(string, string), IWebhookProcessor> _processors;
    private readonly IWebhookProcessor? _fallback;
    private readonly IngestionService _ingestionService;
    private readonly WhitelistService _whitelistService;
    private readonly ILogger<WebhookProcessorFunction> _logger;

    public WebhookProcessorFunction(
        IEnumerable<IWebhookProcessor> processors,
        IngestionService ingestionService,
        WhitelistService whitelistService,
        ILogger<WebhookProcessorFunction> logger)
    {
        var lookup = new Dictionary<(string, string), IWebhookProcessor>();
        IWebhookProcessor? fallback = null;

        foreach (var p in processors)
        {
            if (p.Source == "*" && p.Type == "*")
                fallback = p;
            else
                lookup[(p.Source, p.Type)] = p;
        }

        _processors = lookup;
        _fallback = fallback;
        _ingestionService = ingestionService;
        _whitelistService = whitelistService;
        _logger = logger;
    }

    [Function("WebhookProcessor")]
    public async Task Run(
        [QueueTrigger("webhook-processing", Connection = "StorageConnectionString")]
        string messageText,
        CancellationToken ct)
    {
        var webhook = JsonSerializer.Deserialize<WebhookMessage>(messageText);
        if (webhook == null)
        {
            _logger.LogError("Failed to deserialize webhook message");
            return;
        }

        _logger.LogInformation(
            "Processing {Source}/{Type} webhook for agent {Agent}",
            webhook.Source, webhook.Type, webhook.AgentName);

        var key = (webhook.Source.ToLowerInvariant(), webhook.Type.ToLowerInvariant());

        if (!_processors.TryGetValue(key, out var processor))
            processor = _fallback ?? throw new NotSupportedException(
                $"Unknown source/type: {webhook.Source}/{webhook.Type}");

        var item = await processor.ProcessAsync(webhook, ct);
        if (item == null)
        {
            _logger.LogWarning(
                "Processor returned null for {Source}/{Type}",
                webhook.Source, webhook.Type);
            return;
        }

        // Whitelist check: if the item has a sender, verify they're allowed
        if (!string.IsNullOrEmpty(item.SenderEmail))
        {
            var domain = ExtractDomain(item.SenderEmail);
            if (!await _whitelistService.IsSenderWhitelisted(item.SenderEmail, domain, ct))
            {
                await _ingestionService.StorePendingEmail(item, domain, ct);
                _logger.LogInformation(
                    "Sender {Sender} not whitelisted — stored as pending for {Agent}",
                    item.SenderEmail, webhook.AgentName);
                return;
            }

            await _whitelistService.IncrementEmailCount(item.SenderEmail, domain, ct);
        }

        await _ingestionService.StoreItem(item, ct);

        _logger.LogInformation(
            "Successfully processed {Source}/{Type} for agent {Agent}",
            webhook.Source, webhook.Type, webhook.AgentName);
    }

    private static string ExtractDomain(string email)
    {
        var at = email.IndexOf('@');
        return at >= 0 ? email[(at + 1)..].ToLowerInvariant() : "unknown";
    }
}
