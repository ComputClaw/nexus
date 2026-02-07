using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Queue trigger: fetches full event from Graph API, writes to Items table.
/// </summary>
public sealed class CalendarProcessorFunction
{
    private readonly GraphService _graphService;
    private readonly CalendarIngestionService _calendarIngestionService;
    private readonly ILogger<CalendarProcessorFunction> _logger;

    public CalendarProcessorFunction(
        GraphService graphService,
        CalendarIngestionService calendarIngestionService,
        ILogger<CalendarProcessorFunction> logger)
    {
        _graphService = graphService;
        _calendarIngestionService = calendarIngestionService;
        _logger = logger;
    }

    [Function("CalendarProcessor")]
    public async Task Run(
        [QueueTrigger("calendar-ingest", Connection = "StorageConnectionString")]
        string messageText,
        CancellationToken ct)
    {
        var queueMsg = JsonSerializer.Deserialize<CalendarQueueMessage>(messageText);
        if (queueMsg == null)
        {
            _logger.LogError("Failed to deserialize calendar queue message");
            return;
        }

        _logger.LogInformation("Processing calendar queue message: {ChangeType} {Id}",
            queueMsg.ChangeType, queueMsg.ResourceId);

        // For deletes, Graph won't have the resource — just record the cancellation
        if (queueMsg.ChangeType == "deleted")
        {
            await _calendarIngestionService.ProcessDeletion(queueMsg.ResourceId, ct);
            return;
        }

        // Fetch full event from Graph
        var calendarEvent = await _graphService.FetchEvent(queueMsg.ResourcePath, ct);
        if (calendarEvent == null)
        {
            _logger.LogWarning("Event {Id} not found in Graph. Skipping.", queueMsg.ResourceId);
            return;
        }

        // Process through calendar ingestion service
        await _calendarIngestionService.Process(calendarEvent, queueMsg.ChangeType, ct);
    }
}
