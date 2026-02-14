using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Webhooks.Relays;

public sealed class GenericWebhookRelay : IWebhookRelay
{
    public string Source => "putio";
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "download" };

    private readonly ILogger<GenericWebhookRelay> _logger;

    public GenericWebhookRelay(ILogger<GenericWebhookRelay> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<WebhookMessage>?> RelayAsync(WebhookRelayContext context, CancellationToken ct)
    {
        var message = new WebhookMessage
        {
            AgentName = context.AgentName,
            Source = context.Source,
            Type = context.Type,
            WebhookUrl = context.Request.Url.ToString(),
            NotificationData = JsonDocument.Parse(context.Body).RootElement.Clone(),
            ReceivedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Prepared {Source}/{Type} for {Agent}", context.Source, context.Type, context.AgentName);
        return Task.FromResult<IReadOnlyList<WebhookMessage>?>(new[] { message });
    }
}
