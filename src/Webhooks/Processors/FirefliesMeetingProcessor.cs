using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Webhooks.Processors;

public sealed class FirefliesMeetingProcessor : IWebhookProcessor
{
    public string Source => "fireflies";
    public string Type => "meeting";

    private readonly FirefliesService _firefliesService;
    private readonly ILogger<FirefliesMeetingProcessor> _logger;

    public FirefliesMeetingProcessor(FirefliesService firefliesService, ILogger<FirefliesMeetingProcessor> logger)
    {
        _firefliesService = firefliesService;
        _logger = logger;
    }

    public async Task<IngestionItem?> ProcessAsync(WebhookMessage webhook, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<FirefliesPayload>(
            webhook.NotificationData.GetRawText());

        if (payload == null || string.IsNullOrEmpty(payload.MeetingId))
        {
            _logger.LogWarning("Invalid Fireflies notification");
            return null;
        }

        var meeting = await _firefliesService.FetchTranscript(payload.MeetingId, ct);
        if (meeting == null)
        {
            _logger.LogWarning("Meeting not found in Fireflies: {Id}", payload.MeetingId);
            return null;
        }

        var transcriptText = string.Join("\n",
            meeting.Sentences?.Select(s => $"[{s.SpeakerName}]: {s.Text}") ?? []);

        var meetingPayload = new
        {
            webhook = payload,
            meeting = new
            {
                id = meeting.Id,
                title = meeting.Title,
                dateString = meeting.DateString,
                duration = meeting.Duration,
                participants = meeting.Sentences?.Select(s => s.SpeakerName).Distinct(),
                sentenceCount = meeting.Sentences?.Count ?? 0
            }
        };

        var receivedAt = DateTime.TryParse(meeting.DateString, out var meetingDate)
            ? new DateTimeOffset(meetingDate)
            : DateTimeOffset.UtcNow;

        var blobs = new Dictionary<string, BlobContent>();
        if (!string.IsNullOrEmpty(transcriptText))
        {
            blobs["TranscriptBlob"] = new BlobContent
            {
                Content = transcriptText,
                ContainerName = "meetings",
                Prefix = "transcript",
                Extension = "txt"
            };
        }

        return new IngestionItem
        {
            SourceType = "fireflies-meeting",
            AgentName = webhook.AgentName,
            Payload = meetingPayload,
            ReceivedAt = receivedAt,
            Blobs = blobs.Count > 0 ? blobs : null
        };
    }
}
