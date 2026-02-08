using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Helpers;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Unified webhook endpoint for all external sources.
/// Validates, enqueues, and returns fast 202 response.
/// Route: POST /api/webhook/{agentName}/{source}/{type}
/// </summary>
public sealed class WebhookRelayFunction
{
    private readonly QueueClientFactory _queues;
    private readonly string _graphClientState;
    private readonly string? _firefliesWebhookSecret;
    private readonly ILogger<WebhookRelayFunction> _logger;

    // Valid agent names
    private static readonly HashSet<string> ValidAgents = new(StringComparer.OrdinalIgnoreCase)
    {
        "stewardclaw", "sageclaw", "main", "flickclaw", "puzzlesclaw"
    };

    // Valid source/type combinations
    private static readonly Dictionary<string, HashSet<string>> ValidSourceTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["graph"] = new(StringComparer.OrdinalIgnoreCase) { "email", "calendar" },
            ["fireflies"] = new(StringComparer.OrdinalIgnoreCase) { "meeting" }
        };

    public WebhookRelayFunction(
        QueueClientFactory queues,
        IConfiguration config,
        ILogger<WebhookRelayFunction> logger)
    {
        _queues = queues;
        _graphClientState = config["Graph:ClientState"] ?? "";
        _firefliesWebhookSecret = config["Fireflies:WebhookSecret"];
        _logger = logger;
    }

    [Function("WebhookRelay")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post",
            Route = "webhook/{agentName}/{source}/{type}")]
        HttpRequestData req,
        string agentName,
        string source,
        string type,
        CancellationToken ct)
    {
        // 1. Validate route parameters
        if (!ValidAgents.Contains(agentName))
        {
            _logger.LogWarning("Invalid agent name: {Agent}", agentName);
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync($"Invalid agent: {agentName}", ct);
            return badRequest;
        }

        if (!ValidSourceTypes.TryGetValue(source, out var validTypes) || !validTypes.Contains(type))
        {
            _logger.LogWarning("Invalid source/type: {Source}/{Type}", source, type);
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync($"Invalid source/type: {source}/{type}", ct);
            return badRequest;
        }

        // 2. Handle Graph validation handshake
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var validationToken = query["validationToken"];
        if (!string.IsNullOrEmpty(validationToken))
        {
            _logger.LogInformation("Graph subscription validation for {Agent}/{Source}/{Type}",
                agentName, source, type);
            var validationResponse = req.CreateResponse(HttpStatusCode.OK);
            validationResponse.Headers.Add("Content-Type", "text/plain");
            await validationResponse.WriteStringAsync(validationToken, ct);
            return validationResponse;
        }

        // 3. Read body
        var body = await req.ReadAsStringAsync();
        if (string.IsNullOrEmpty(body))
        {
            _logger.LogWarning("Empty webhook body");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        // 4. Process based on source
        try
        {
            if (source.Equals("graph", StringComparison.OrdinalIgnoreCase))
            {
                await ProcessGraphWebhook(req, body, agentName, source, type, ct);
            }
            else if (source.Equals("fireflies", StringComparison.OrdinalIgnoreCase))
            {
                if (!await ProcessFirefliesWebhook(req, body, agentName, source, type, ct))
                {
                    return req.CreateResponse(HttpStatusCode.Unauthorized);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in webhook body");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        // 5. Return fast 202 Accepted
        return req.CreateResponse(HttpStatusCode.Accepted);
    }

    private async Task ProcessGraphWebhook(
        HttpRequestData req, string body,
        string agentName, string source, string type,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<GraphNotificationPayload>(body);
        if (payload?.Value == null || payload.Value.Count == 0)
        {
            _logger.LogWarning("Empty Graph notification payload");
            return;
        }

        foreach (var notification in payload.Value)
        {
            // Validate client state
            if (!string.IsNullOrEmpty(_graphClientState) &&
                notification.ClientState != _graphClientState)
            {
                _logger.LogWarning("Invalid Graph clientState, skipping notification");
                continue;
            }

            // Skip lifecycle events (handled by LifecycleNotificationFunction)
            if (!string.IsNullOrEmpty(notification.LifecycleEvent))
            {
                continue;
            }

            // Enqueue
            var webhookMessage = new WebhookMessage
            {
                AgentName = agentName,
                Source = source,
                Type = type,
                WebhookUrl = req.Url.ToString(),
                NotificationData = JsonSerializer.SerializeToElement(notification),
                ReceivedAt = DateTime.UtcNow
            };

            var queueMsg = JsonSerializer.Serialize(webhookMessage);
            await _queues.WebhookQueue.SendMessageAsync(queueMsg, ct);

            _logger.LogInformation(
                "Enqueued {Source}/{Type} for {Agent}: {ChangeType} {Id}",
                source, type, agentName,
                notification.ChangeType, notification.ResourceData?.Id);
        }
    }

    private async Task<bool> ProcessFirefliesWebhook(
        HttpRequestData req, string body,
        string agentName, string source, string type,
        CancellationToken ct)
    {
        // Validate HMAC signature
        var signature = req.Headers.TryGetValues("x-hub-signature", out var values)
            ? values.FirstOrDefault() : null;

        if (!string.IsNullOrEmpty(_firefliesWebhookSecret) &&
            !SignatureValidator.Verify(body, signature, _firefliesWebhookSecret))
        {
            _logger.LogWarning("Invalid Fireflies webhook signature");
            return false;
        }

        // Enqueue
        var webhookMessage = new WebhookMessage
        {
            AgentName = agentName,
            Source = source,
            Type = type,
            WebhookUrl = req.Url.ToString(),
            NotificationData = JsonDocument.Parse(body).RootElement.Clone(),
            ReceivedAt = DateTime.UtcNow
        };

        var queueMsg = JsonSerializer.Serialize(webhookMessage);
        await _queues.WebhookQueue.SendMessageAsync(queueMsg, ct);

        _logger.LogInformation("Enqueued {Source}/{Type} for {Agent}", source, type, agentName);
        return true;
    }
}
