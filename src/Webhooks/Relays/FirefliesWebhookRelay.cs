using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Helpers;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Webhooks.Relays;

public sealed class FirefliesWebhookRelay : IWebhookRelay
{
    public string Source => "fireflies";
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "meeting" };

    private readonly string? _webhookSecret;
    private readonly ILogger<FirefliesWebhookRelay> _logger;

    public FirefliesWebhookRelay(IConfiguration config, ILogger<FirefliesWebhookRelay> logger)
    {
        _webhookSecret = config["Fireflies:WebhookSecret"];
        _logger = logger;
    }

    public Task<IReadOnlyList<WebhookMessage>?> RelayAsync(WebhookRelayContext context, CancellationToken ct)
    {
        var signature = context.Request.Headers.TryGetValues("x-hub-signature", out var values)
            ? values.FirstOrDefault() : null;

        if (!string.IsNullOrEmpty(_webhookSecret) &&
            !SignatureValidator.Verify(context.Body, signature, _webhookSecret))
        {
            _logger.LogWarning("Invalid Fireflies webhook signature");
            return Task.FromResult<IReadOnlyList<WebhookMessage>?>(null);
        }

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
