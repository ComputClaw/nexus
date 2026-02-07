using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Thin router: validates Graph notifications and enqueues to the appropriate queue.
/// Does NOT fetch data — that happens in the queue processors.
/// </summary>
public sealed class GraphNotificationFunction
{
    private readonly QueueClientFactory _queues;
    private readonly string _clientState;
    private readonly ILogger<GraphNotificationFunction> _logger;

    public GraphNotificationFunction(
        QueueClientFactory queues,
        IConfiguration config,
        ILogger<GraphNotificationFunction> logger)
    {
        _queues = queues;
        _clientState = config["Graph:ClientState"] ?? "";
        _logger = logger;
    }

    [Function("GraphNotification")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "notifications")]
        HttpRequestData req,
        CancellationToken ct)
    {
        // 1. Subscription validation handshake
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var validationToken = query["validationToken"];
        if (!string.IsNullOrEmpty(validationToken))
        {
            _logger.LogInformation("Graph subscription validation handshake");
            var validationResponse = req.CreateResponse(HttpStatusCode.OK);
            validationResponse.Headers.Add("Content-Type", "text/plain");
            await validationResponse.WriteStringAsync(validationToken, ct);
            return validationResponse;
        }

        // 2. Parse notification payload
        var body = await req.ReadAsStringAsync();
        if (string.IsNullOrEmpty(body))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var notifications = JsonSerializer.Deserialize<GraphNotificationPayload>(body);
        if (notifications?.Value == null)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        // 3. Route each notification to the correct queue
        foreach (var notification in notifications.Value)
        {
            // Validate clientState
            if (notification.ClientState != _clientState)
            {
                _logger.LogWarning("Invalid clientState in notification, skipping");
                continue;
            }

            var resourceType = notification.ResourceData?.ODataType;

            if (resourceType == "#Microsoft.Graph.Message")
            {
                var queueMsg = JsonSerializer.Serialize(new EmailQueueMessage
                {
                    ResourcePath = notification.Resource,
                    ChangeType = notification.ChangeType,
                    ResourceId = notification.ResourceData!.Id,
                    SubscriptionId = notification.SubscriptionId
                });
                await _queues.EmailQueue.SendMessageAsync(queueMsg, ct);
                _logger.LogInformation("Enqueued email notification: {ChangeType} {Id}",
                    notification.ChangeType, notification.ResourceData.Id);
            }
            else if (resourceType == "#Microsoft.Graph.Event")
            {
                var queueMsg = JsonSerializer.Serialize(new CalendarQueueMessage
                {
                    ResourcePath = notification.Resource,
                    ChangeType = notification.ChangeType,
                    ResourceId = notification.ResourceData!.Id,
                    SubscriptionId = notification.SubscriptionId
                });
                await _queues.CalendarQueue.SendMessageAsync(queueMsg, ct);
                _logger.LogInformation("Enqueued calendar notification: {ChangeType} {Id}",
                    notification.ChangeType, notification.ResourceData.Id);
            }
            else
            {
                _logger.LogWarning("Unknown resource type in notification: {Type}", resourceType);
            }
        }

        // 4. Respond 202 — fast, no external API calls
        return req.CreateResponse(HttpStatusCode.Accepted);
    }
}
