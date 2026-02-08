using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Unified queue processor for all webhook types.
/// Uses simplified ingestion with unified Items table schema.
/// </summary>
public sealed class WebhookProcessorFunction
{
    private readonly GraphService _graphService;
    private readonly FirefliesService? _firefliesService;
    private readonly SimpleIngestionService _ingestionService;
    private readonly ILogger<WebhookProcessorFunction> _logger;

    public WebhookProcessorFunction(
        GraphService graphService,
        SimpleIngestionService ingestionService,
        ILogger<WebhookProcessorFunction> logger,
        FirefliesService? firefliesService = null)
    {
        _graphService = graphService;
        _firefliesService = firefliesService;
        _ingestionService = ingestionService;
        _logger = logger;
    }

    [Function("WebhookProcessor")]
    public async Task Run(
        [QueueTrigger("webhook-processing", Connection = "StorageConnectionString")]
        string messageText,
        CancellationToken ct)
    {
        var webhook = JsonSerializer.Deserialize<WebhookMessage>(messageText);
        if (webhook == null)
        {
            _logger.LogError("Failed to deserialize webhook message");
            return;
        }

        _logger.LogInformation(
            "Processing {Source}/{Type} webhook for agent {Agent}",
            webhook.Source, webhook.Type, webhook.AgentName);

        try
        {
            var result = (webhook.Source.ToLowerInvariant(), webhook.Type.ToLowerInvariant()) switch
            {
                ("graph", "email") => await ProcessGraphEmail(webhook, ct),
                ("graph", "calendar") => await ProcessGraphCalendar(webhook, ct),
                ("fireflies", "meeting") => await ProcessFirefliesMeeting(webhook, ct),
                ("github", "release") => await ProcessGitHubRelease(webhook, ct),
                ("putio", "download") => await ProcessGenericWebhook(webhook, ct),
                _ => throw new NotSupportedException($"Unknown source/type: {webhook.Source}/{webhook.Type}")
            };

            if (result)
            {
                _logger.LogInformation(
                    "Successfully processed {Source}/{Type} for agent {Agent}",
                    webhook.Source, webhook.Type, webhook.AgentName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process {Source}/{Type} for agent {Agent}",
                webhook.Source, webhook.Type, webhook.AgentName);
            throw; // Re-throw for retry/poison queue
        }
    }

    private async Task<bool> ProcessGraphEmail(WebhookMessage webhook, CancellationToken ct)
    {
        var notification = JsonSerializer.Deserialize<GraphNotification>(
            webhook.NotificationData.GetRawText());

        if (notification == null)
        {
            _logger.LogWarning("Failed to parse Graph email notification");
            return false;
        }

        // Fetch full message from Graph
        var message = await _graphService.FetchMessage(notification.Resource, ct);
        if (message == null)
        {
            _logger.LogWarning("Message not found in Graph: {Id}", notification.ResourceData?.Id);
            return false;
        }

        // Create simplified payload with notification + message data
        var payload = new
        {
            notification = notification,
            message = new
            {
                id = message.Id,
                subject = message.Subject,
                from = message.From?.EmailAddress?.Address,
                to = message.ToRecipients?.Select(r => r.EmailAddress?.Address),
                cc = message.CcRecipients?.Select(r => r.EmailAddress?.Address),
                conversationId = message.ConversationId,
                receivedDateTime = message.ReceivedDateTime,
                sentDateTime = message.SentDateTime,
                bodyPreview = message.BodyPreview,
                hasAttachments = message.HasAttachments
            }
        };

        // Extract body content for blob storage
        var bodyContent = message.Body?.Content;
        var receivedAt = message.ReceivedDateTime ?? message.SentDateTime ?? DateTimeOffset.UtcNow;

        await _ingestionService.StoreEmail(
            payload,
            webhook.AgentName,
            bodyContent,
            null, // TODO: Handle attachments
            receivedAt,
            ct);

        return true;
    }

    private async Task<bool> ProcessGraphCalendar(WebhookMessage webhook, CancellationToken ct)
    {
        var notification = JsonSerializer.Deserialize<GraphNotification>(
            webhook.NotificationData.GetRawText());

        if (notification == null)
        {
            _logger.LogWarning("Failed to parse Graph calendar notification");
            return false;
        }

        // Fetch full event from Graph
        var calendarEvent = await _graphService.FetchEvent(notification.Resource, ct);
        if (calendarEvent == null)
        {
            _logger.LogWarning("Event not found in Graph: {Id}", notification.ResourceData?.Id);
            return false;
        }

        // Create simplified payload
        var payload = new
        {
            notification = notification,
            calendarEvent = new
            {
                id = calendarEvent.Id,
                subject = calendarEvent.Subject,
                start = calendarEvent.Start,
                end = calendarEvent.End,
                location = calendarEvent.Location?.DisplayName,
                organizer = calendarEvent.Organizer?.EmailAddress?.Address,
                attendees = calendarEvent.Attendees?.Select(a => new
                {
                    email = a.EmailAddress?.Address,
                    name = a.EmailAddress?.Name,
                    status = a.Status?.Response?.ToString()
                }),
                bodyPreview = calendarEvent.BodyPreview,
                isAllDay = calendarEvent.IsAllDay,
                changeKey = calendarEvent.ChangeKey,
                createdDateTime = calendarEvent.CreatedDateTime,
                lastModifiedDateTime = calendarEvent.LastModifiedDateTime
            }
        };

        var receivedAt = calendarEvent.LastModifiedDateTime ?? DateTimeOffset.UtcNow;

        await _ingestionService.StoreItem(
            payload,
            "graph-calendar",
            webhook.AgentName,
            receivedAt,
            null,
            ct);

        return true;
    }

    private async Task<bool> ProcessFirefliesMeeting(WebhookMessage webhook, CancellationToken ct)
    {
        if (_firefliesService == null)
        {
            _logger.LogWarning("Fireflies service not configured");
            return false;
        }

        var payload = JsonSerializer.Deserialize<FirefliesPayload>(
            webhook.NotificationData.GetRawText());

        if (payload == null || string.IsNullOrEmpty(payload.MeetingId))
        {
            _logger.LogWarning("Invalid Fireflies notification");
            return false;
        }

        // Fetch transcript from Fireflies
        var meeting = await _firefliesService.FetchTranscript(payload.MeetingId, ct);
        if (meeting == null)
        {
            _logger.LogWarning("Meeting not found in Fireflies: {Id}", payload.MeetingId);
            return false;
        }

        // Build transcript text for blob storage
        var transcriptText = string.Join("\n", 
            meeting.Sentences?.Select(s => $"[{s.SpeakerName}]: {s.Text}") ?? []);

        // Create simplified payload
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

        await _ingestionService.StoreMeeting(
            meetingPayload,
            webhook.AgentName,
            transcriptText,
            receivedAt,
            ct);

        return true;
    }

    private async Task<bool> ProcessGitHubRelease(WebhookMessage webhook, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(
            webhook.NotificationData.GetRawText());

        // Extract key release info
        var releasePayload = new
        {
            action = payload.TryGetProperty("action", out var action) ? action.GetString() : null,
            release = payload.TryGetProperty("release", out var release) ? new
            {
                id = release.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                tagName = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null,
                name = release.TryGetProperty("name", out var name) ? name.GetString() : null,
                body = release.TryGetProperty("body", out var body) ? body.GetString() : null,
                htmlUrl = release.TryGetProperty("html_url", out var url) ? url.GetString() : null,
                prerelease = release.TryGetProperty("prerelease", out var pre) ? pre.GetBoolean() : false,
                publishedAt = release.TryGetProperty("published_at", out var pub) ? pub.GetString() : null,
                author = release.TryGetProperty("author", out var auth) && auth.TryGetProperty("login", out var login) 
                    ? login.GetString() : null
            } : null,
            repository = payload.TryGetProperty("repository", out var repo) ? new
            {
                fullName = repo.TryGetProperty("full_name", out var fn) ? fn.GetString() : null,
                htmlUrl = repo.TryGetProperty("html_url", out var rUrl) ? rUrl.GetString() : null
            } : null
        };

        var publishedAt = !string.IsNullOrEmpty(releasePayload.release?.publishedAt) &&
                          DateTimeOffset.TryParse(releasePayload.release.publishedAt, out var parsed)
            ? parsed
            : webhook.ReceivedAt;

        await _ingestionService.StoreGitHubRelease(
            releasePayload,
            webhook.AgentName,
            publishedAt,
            ct);

        return true;
    }

    private async Task<bool> ProcessGenericWebhook(WebhookMessage webhook, CancellationToken ct)
    {
        // Store full webhook payload as-is
        var payload = JsonSerializer.Deserialize<JsonElement>(
            webhook.NotificationData.GetRawText());

        await _ingestionService.StoreGeneric(
            payload,
            webhook.Source,
            webhook.Type,
            webhook.AgentName,
            webhook.ReceivedAt,
            ct);

        return true;
    }
}
