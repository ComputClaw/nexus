using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Handles Graph lifecycle events: reauthorization, subscription removed, missed notifications.
/// </summary>
public sealed class LifecycleNotificationFunction
{
    private readonly SubscriptionService _subscriptionService;
    private readonly ILogger<LifecycleNotificationFunction> _logger;

    public LifecycleNotificationFunction(
        SubscriptionService subscriptionService,
        ILogger<LifecycleNotificationFunction> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [Function("LifecycleNotification")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "lifecycle")]
        HttpRequestData req,
        CancellationToken ct)
    {
        // Validation handshake
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var validationToken = query["validationToken"];
        if (!string.IsNullOrEmpty(validationToken))
        {
            var validationResponse = req.CreateResponse(HttpStatusCode.OK);
            validationResponse.Headers.Add("Content-Type", "text/plain");
            await validationResponse.WriteStringAsync(validationToken, ct);
            return validationResponse;
        }

        var body = await req.ReadAsStringAsync();
        if (string.IsNullOrEmpty(body))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var notifications = JsonSerializer.Deserialize<GraphNotificationPayload>(body);
        if (notifications?.Value == null)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        foreach (var notification in notifications.Value)
        {
            _logger.LogInformation(
                "Lifecycle event: {Event} for subscription {Id}",
                notification.LifecycleEvent, notification.SubscriptionId);

            switch (notification.LifecycleEvent)
            {
                case "reauthorizationRequired":
                    await _subscriptionService.Reauthorize(notification.SubscriptionId, ct);
                    break;

                case "subscriptionRemoved":
                    await _subscriptionService.Recreate(notification.SubscriptionId, ct);
                    break;

                case "missed":
                    _logger.LogWarning(
                        "Missed notifications for subscription {Id}. Delta query sync should pick these up.",
                        notification.SubscriptionId);
                    break;

                default:
                    _logger.LogWarning("Unknown lifecycle event: {Event}", notification.LifecycleEvent);
                    break;
            }
        }

        return req.CreateResponse(HttpStatusCode.Accepted);
    }
}
