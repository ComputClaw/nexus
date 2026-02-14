using Nexus.Ingest.Models;

namespace Nexus.Ingest.Webhooks.Processors;

public interface IWebhookProcessor
{
    string Source { get; }
    string Type { get; }
    Task<IngestionItem?> ProcessAsync(WebhookMessage webhook, CancellationToken ct);
}
