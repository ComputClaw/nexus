using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Graph;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Webhooks.Processors;

public sealed class GraphCalendarProcessor : IWebhookProcessor
{
    public string Source => "graph";
    public string Type => "calendar";

    private readonly GraphService _graphService;
    private readonly ILogger<GraphCalendarProcessor> _logger;

    public GraphCalendarProcessor(GraphService graphService, ILogger<GraphCalendarProcessor> logger)
    {
        _graphService = graphService;
        _logger = logger;
    }

    public async Task<IngestionItem?> ProcessAsync(WebhookMessage webhook, CancellationToken ct)
    {
        var notification = JsonSerializer.Deserialize<GraphNotification>(
            webhook.NotificationData.GetRawText());

        if (notification == null)
        {
            _logger.LogWarning("Failed to parse Graph calendar notification");
            return null;
        }

        var calendarEvent = await _graphService.FetchEvent(notification.Resource, ct);
        if (calendarEvent == null)
        {
            _logger.LogWarning("Event not found in Graph: {Id}", notification.ResourceData?.Id);
            return null;
        }

        var payload = new
        {
            notification,
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

        return new IngestionItem
        {
            SourceType = "graph-calendar",
            AgentName = webhook.AgentName,
            Payload = payload,
            ReceivedAt = receivedAt
        };
    }
}
