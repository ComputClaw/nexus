using Microsoft.Azure.Functions.Worker.Http;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Webhooks.Relays;

public interface IWebhookRelay
{
    string Source { get; }
    IReadOnlySet<string> SupportedTypes { get; }
    Task<IReadOnlyList<WebhookMessage>?> RelayAsync(WebhookRelayContext context, CancellationToken ct);
}

public sealed record WebhookRelayContext(
    HttpRequestData Request, string Body, string AgentName, string Source, string Type);
