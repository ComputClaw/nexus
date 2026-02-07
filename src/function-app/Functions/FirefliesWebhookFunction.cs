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
/// Thin router: validates Fireflies HMAC signature and enqueues to meeting-ingest queue.
/// </summary>
public sealed class FirefliesWebhookFunction
{
    private readonly QueueClientFactory _queues;
    private readonly string? _webhookSecret;
    private readonly ILogger<FirefliesWebhookFunction> _logger;

    public FirefliesWebhookFunction(
        QueueClientFactory queues,
        IConfiguration config,
        ILogger<FirefliesWebhookFunction> logger)
    {
        _queues = queues;
        _webhookSecret = config["Fireflies:WebhookSecret"];
        _logger = logger;
    }

    [Function("FirefliesWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "fireflies")]
        HttpRequestData req,
        CancellationToken ct)
    {
        var body = await req.ReadAsStringAsync();
        if (string.IsNullOrEmpty(body))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        // Verify HMAC signature
        var signature = req.Headers.TryGetValues("x-hub-signature", out var values)
            ? values.FirstOrDefault() : null;

        if (!SignatureValidator.Verify(body, signature, _webhookSecret))
        {
            _logger.LogWarning("Invalid Fireflies webhook signature");
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        // Enqueue for processing
        var payload = JsonSerializer.Deserialize<FirefliesPayload>(body);
        if (payload == null || string.IsNullOrEmpty(payload.MeetingId))
        {
            _logger.LogWarning("Invalid Fireflies payload");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        var queueMsg = JsonSerializer.Serialize(new MeetingQueueMessage
        {
            MeetingId = payload.MeetingId,
            EventType = payload.EventType
        });
        await _queues.MeetingQueue.SendMessageAsync(queueMsg, ct);

        _logger.LogInformation("Enqueued Fireflies meeting: {MeetingId}", payload.MeetingId);

        return req.CreateResponse(HttpStatusCode.OK);
    }
}
