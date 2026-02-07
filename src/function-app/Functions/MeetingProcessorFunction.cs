using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Queue trigger: fetches full transcript from Fireflies, stores blob, writes to Items table.
/// </summary>
public sealed class MeetingProcessorFunction
{
    private readonly FirefliesService _firefliesService;
    private readonly MeetingIngestionService _meetingIngestionService;
    private readonly ILogger<MeetingProcessorFunction> _logger;

    public MeetingProcessorFunction(
        FirefliesService firefliesService,
        MeetingIngestionService meetingIngestionService,
        ILogger<MeetingProcessorFunction> logger)
    {
        _firefliesService = firefliesService;
        _meetingIngestionService = meetingIngestionService;
        _logger = logger;
    }

    [Function("MeetingProcessor")]
    public async Task Run(
        [QueueTrigger("meeting-ingest", Connection = "StorageConnectionString")]
        string messageText,
        CancellationToken ct)
    {
        var queueMsg = JsonSerializer.Deserialize<MeetingQueueMessage>(messageText);
        if (queueMsg == null)
        {
            _logger.LogError("Failed to deserialize meeting queue message");
            return;
        }

        _logger.LogInformation("Processing meeting: {MeetingId}", queueMsg.MeetingId);

        var transcript = await _firefliesService.FetchTranscript(queueMsg.MeetingId, ct);
        if (transcript == null)
        {
            _logger.LogWarning("Transcript {Id} not found in Fireflies. Skipping.", queueMsg.MeetingId);
            return;
        }

        await _meetingIngestionService.Process(transcript, ct);
    }
}
