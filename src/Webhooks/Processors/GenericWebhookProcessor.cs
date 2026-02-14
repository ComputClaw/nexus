using System.Text.Json;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Webhooks.Processors;

public sealed class GenericWebhookProcessor : IWebhookProcessor
{
    public string Source => "*";
    public string Type => "*";

    public Task<IngestionItem?> ProcessAsync(WebhookMessage webhook, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(
            webhook.NotificationData.GetRawText());

        var item = new IngestionItem
        {
            SourceType = $"{webhook.Source}-{webhook.Type}",
            AgentName = webhook.AgentName,
            Payload = payload,
            ReceivedAt = new DateTimeOffset(webhook.ReceivedAt, TimeSpan.Zero)
        };

        return Task.FromResult<IngestionItem?>(item);
    }
}
