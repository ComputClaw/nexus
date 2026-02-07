using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// One-shot HTTP trigger to create initial Graph subscriptions.
/// Call once after deployment (or when subscriptions need recreation).
/// </summary>
public sealed class SubscriptionBootstrapFunction
{
    private readonly SubscriptionService _subscriptionService;
    private readonly string _userId;
    private readonly ILogger<SubscriptionBootstrapFunction> _logger;

    public SubscriptionBootstrapFunction(
        SubscriptionService subscriptionService,
        IConfiguration config,
        ILogger<SubscriptionBootstrapFunction> logger)
    {
        _subscriptionService = subscriptionService;
        _userId = config["Graph:UserId"] ?? throw new InvalidOperationException("Graph:UserId not configured");
        _logger = logger;
    }

    [Function("SubscriptionBootstrap")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "subscriptions/bootstrap")]
        HttpRequestData req,
        CancellationToken ct)
    {
        _logger.LogInformation("Bootstrapping Graph subscriptions for user {UserId}", _userId);

        try
        {
            var results = new List<object>();

            // 1. Inbox messages
            _logger.LogInformation("Creating inbox subscription...");
            var inbox = await _subscriptionService.Create(
                resource: $"users/{_userId}/mailFolders('inbox')/messages",
                changeTypes: "created,updated",
                ct);
            results.Add(new { type = "inbox", subscriptionId = inbox });

            // 2. Sent items
            _logger.LogInformation("Creating sent items subscription...");
            var sent = await _subscriptionService.Create(
                resource: $"users/{_userId}/mailFolders('sentitems')/messages",
                changeTypes: "created",
                ct);
            results.Add(new { type = "sent", subscriptionId = sent });

            // 3. Calendar events
            _logger.LogInformation("Creating calendar subscription...");
            var calendar = await _subscriptionService.Create(
                resource: $"users/{_userId}/events",
                changeTypes: "created,updated,deleted",
                ct);
            results.Add(new { type = "calendar", subscriptionId = calendar });

            _logger.LogInformation("Bootstrapped {Count} Graph subscriptions", results.Count);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { subscriptions = results }, ct);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bootstrap subscriptions");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new
            {
                error = ex.Message,
                type = ex.GetType().Name,
                inner = ex.InnerException?.Message
            }, ct);
            return errorResponse;
        }
    }
}
