using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Nexus.Ingest.Webhooks;

/// <summary>
/// Poison queue handler: logs failed messages for observability.
/// When a queue message fails 5 times, Azure moves it to {queue-name}-poison.
/// </summary>
public sealed class PoisonQueueHandlers
{
    private readonly ILogger<PoisonQueueHandlers> _logger;

    public PoisonQueueHandlers(ILogger<PoisonQueueHandlers> logger)
    {
        _logger = logger;
    }

    [Function("WebhookPoisonHandler")]
    public Task WebhookPoison(
        [QueueTrigger("webhook-processing-poison", Connection = "StorageConnectionString")]
        string messageText)
    {
        _logger.LogError("Poison queue message in webhook-processing: {Message}", messageText);
        return Task.CompletedTask;
    }
}
