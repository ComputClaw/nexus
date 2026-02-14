using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Helpers;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Webhooks.Relays;

public sealed class GitHubWebhookRelay : IWebhookRelay
{
    public string Source => "github";
    public IReadOnlySet<string> SupportedTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "release" };

    private readonly string? _webhookSecret;
    private readonly ILogger<GitHubWebhookRelay> _logger;

    public GitHubWebhookRelay(IConfiguration config, ILogger<GitHubWebhookRelay> logger)
    {
        _webhookSecret = config["GitHub:WebhookSecret"];
        _logger = logger;
    }

    public Task<IReadOnlyList<WebhookMessage>?> RelayAsync(WebhookRelayContext context, CancellationToken ct)
    {
        var signature = context.Request.Headers.TryGetValues("x-hub-signature-256", out var values)
            ? values.FirstOrDefault()?.Replace("sha256=", "") : null;

        if (!string.IsNullOrEmpty(_webhookSecret) &&
            !SignatureValidator.Verify(context.Body, signature, _webhookSecret))
        {
            _logger.LogWarning("Invalid GitHub webhook signature");
            return Task.FromResult<IReadOnlyList<WebhookMessage>?>(null);
        }

        var payload = JsonSerializer.Deserialize<JsonElement>(context.Body);
        var action = payload.TryGetProperty("action", out var actionProp)
            ? actionProp.GetString() : null;

        if (action != "published")
        {
            _logger.LogInformation("Ignoring GitHub {Action} action for {Agent}", action, context.AgentName);
            return Task.FromResult<IReadOnlyList<WebhookMessage>?>(Array.Empty<WebhookMessage>());
        }

        var message = new WebhookMessage
        {
            AgentName = context.AgentName,
            Source = context.Source,
            Type = context.Type,
            WebhookUrl = context.Request.Url.ToString(),
            NotificationData = payload.Clone(),
            ReceivedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Prepared GitHub {Type} for {Agent}: {Action}",
            context.Type, context.AgentName, action);
        return Task.FromResult<IReadOnlyList<WebhookMessage>?>(new[] { message });
    }
}
