using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Webhooks.Relays;

public sealed class GraphWebhookRelay : IWebhookRelay
{
    public string Source => "graph";
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "email", "calendar" };

    private readonly string _graphClientState;
    private readonly ILogger<GraphWebhookRelay> _logger;

    public GraphWebhookRelay(IConfiguration config, ILogger<GraphWebhookRelay> logger)
    {
        _graphClientState = config["Graph:ClientState"] ?? "";
        _logger = logger;
    }

    public Task<IReadOnlyList<WebhookMessage>?> RelayAsync(WebhookRelayContext context, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<GraphNotificationPayload>(context.Body);
        if (payload?.Value == null || payload.Value.Count == 0)
        {
            _logger.LogWarning("Empty Graph notification payload");
            return Task.FromResult<IReadOnlyList<WebhookMessage>?>(Array.Empty<WebhookMessage>());
        }

        var messages = new List<WebhookMessage>();

        foreach (var notification in payload.Value)
        {
            if (!string.IsNullOrEmpty(_graphClientState) &&
                notification.ClientState != _graphClientState)
            {
                _logger.LogWarning("Invalid Graph clientState, skipping notification");
                continue;
            }

            if (!string.IsNullOrEmpty(notification.LifecycleEvent))
                continue;

            messages.Add(new WebhookMessage
            {
                AgentName = context.AgentName,
                Source = context.Source,
                Type = context.Type,
                WebhookUrl = context.Request.Url.ToString(),
                NotificationData = JsonSerializer.SerializeToElement(notification),
                ReceivedAt = DateTime.UtcNow
            });

            _logger.LogInformation(
                "Prepared {Source}/{Type} for {Agent}: {ChangeType} {Id}",
                context.Source, context.Type, context.AgentName,
                notification.ChangeType, notification.ResourceData?.Id);
        }

        return Task.FromResult<IReadOnlyList<WebhookMessage>?>(messages);
    }
}
