using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Services;
using Nexus.Ingest.Webhooks.Relays;

namespace Nexus.Ingest.Webhooks;

/// <summary>
/// Unified webhook endpoint for all external sources.
/// Validates, delegates to source-specific relays, enqueues, and returns fast 202 response.
/// Route: POST /api/webhook/{agentName}/{source}/{type}
/// </summary>
public sealed class WebhookRelayFunction
{
    private readonly Dictionary<string, IWebhookRelay> _relays;
    private readonly Dictionary<string, HashSet<string>> _validSourceTypes;
    private readonly QueueClientFactory _queues;
    private readonly ILogger<WebhookRelayFunction> _logger;

    // Valid agent names
    private static readonly HashSet<string> ValidAgents = new(StringComparer.OrdinalIgnoreCase)
    {
        "stewardclaw", "sageclaw", "main", "flickclaw", "puzzlesclaw", "comput"
    };

    public WebhookRelayFunction(
        IEnumerable<IWebhookRelay> relays,
        QueueClientFactory queues,
        ILogger<WebhookRelayFunction> logger)
    {
        _queues = queues;
        _logger = logger;

        _relays = new(StringComparer.OrdinalIgnoreCase);
        _validSourceTypes = new(StringComparer.OrdinalIgnoreCase);

        foreach (var relay in relays)
        {
            _relays[relay.Source] = relay;
            _validSourceTypes[relay.Source] =
                new HashSet<string>(relay.SupportedTypes, StringComparer.OrdinalIgnoreCase);
        }
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

        if (!_validSourceTypes.TryGetValue(source, out var validTypes) || !validTypes.Contains(type))
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

        // 4. Delegate to relay
        try
        {
            var relay = _relays[source];
            var context = new WebhookRelayContext(req, body, agentName, source, type);
            var messages = await relay.RelayAsync(context, ct);

            // null → 401 (auth failure)
            if (messages == null)
                return req.CreateResponse(HttpStatusCode.Unauthorized);

            // Enqueue all messages
            foreach (var message in messages)
            {
                var queueMsg = JsonSerializer.Serialize(message);
                await _queues.WebhookQueue.SendMessageAsync(queueMsg, ct);
            }

            if (messages.Count > 0)
            {
                _logger.LogInformation(
                    "Enqueued {Count} {Source}/{Type} message(s) for {Agent}",
                    messages.Count, source, type, agentName);
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
}
