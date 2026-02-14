using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Nexus.Ingest.Graph;

/// <summary>
/// Timer trigger: renews Graph subscriptions daily at 08:00 UTC.
/// </summary>
public sealed class SubscriptionTimerFunction
{
    private readonly SubscriptionService _subscriptionService;
    private readonly ILogger<SubscriptionTimerFunction> _logger;

    public SubscriptionTimerFunction(
        SubscriptionService subscriptionService,
        ILogger<SubscriptionTimerFunction> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [Function("SubscriptionRenewal")]
    public async Task Run(
        [TimerTrigger("0 0 8 * * *")] TimerInfo timer,
        CancellationToken ct)
    {
        _logger.LogInformation("Starting subscription renewal check");

        var subscriptions = await _subscriptionService.GetActiveSubscriptions(ct);

        foreach (var sub in subscriptions)
        {
            try
            {
                await _subscriptionService.Renew(sub.RowKey!, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to renew subscription {Id}. Recreating.", sub.RowKey);
                try
                {
                    await _subscriptionService.Recreate(sub.RowKey!, ct);
                }
                catch (Exception recreateEx)
                {
                    _logger.LogError(recreateEx, "Failed to recreate subscription {Id}", sub.RowKey);
                }
            }
        }

        _logger.LogInformation("Subscription renewal complete. Processed {Count} subscriptions",
            subscriptions.Count);
    }
}
