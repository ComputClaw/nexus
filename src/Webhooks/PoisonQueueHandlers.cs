using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Nexus.Ingest.Webhooks;

/// <summary>
/// Poison queue handlers: log failed messages for observability.
/// When a queue message fails 5 times, Azure moves it to {queue-name}-poison.
/// </summary>
public sealed class PoisonQueueHandlers
{
    private readonly ILogger<PoisonQueueHandlers> _logger;

    public PoisonQueueHandlers(ILogger<PoisonQueueHandlers> logger)
    {
        _logger = logger;
    }

    [Function("EmailPoisonHandler")]
    public Task EmailPoison(
        [QueueTrigger("email-ingest-poison", Connection = "StorageConnectionString")]
        string messageText)
    {
        _logger.LogError("Poison queue message in email-ingest: {Message}", messageText);
        return Task.CompletedTask;
    }

    [Function("CalendarPoisonHandler")]
    public Task CalendarPoison(
        [QueueTrigger("calendar-ingest-poison", Connection = "StorageConnectionString")]
        string messageText)
    {
        _logger.LogError("Poison queue message in calendar-ingest: {Message}", messageText);
        return Task.CompletedTask;
    }

    [Function("MeetingPoisonHandler")]
    public Task MeetingPoison(
        [QueueTrigger("meeting-ingest-poison", Connection = "StorageConnectionString")]
        string messageText)
    {
        _logger.LogError("Poison queue message in meeting-ingest: {Message}", messageText);
        return Task.CompletedTask;
    }
}
