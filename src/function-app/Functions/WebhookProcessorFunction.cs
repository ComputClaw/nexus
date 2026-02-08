using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Unified queue processor for all webhook types.
/// Routes to appropriate ingestion service based on source/type.
/// </summary>
public sealed class WebhookProcessorFunction
{
    private readonly GraphService _graphService;
    private readonly FirefliesService? _firefliesService;
    private readonly EmailIngestionService _emailService;
    private readonly CalendarIngestionService _calendarService;
    private readonly MeetingIngestionService _meetingService;
    private readonly OpenClawService _openClawService;
    private readonly TableClient _itemsTable;
    private readonly ILogger<WebhookProcessorFunction> _logger;

    public WebhookProcessorFunction(
        GraphService graphService,
        EmailIngestionService emailService,
        CalendarIngestionService calendarService,
        MeetingIngestionService meetingService,
        OpenClawService openClawService,
        TableServiceClient tableService,
        ILogger<WebhookProcessorFunction> logger,
        FirefliesService? firefliesService = null)
    {
        _graphService = graphService;
        _firefliesService = firefliesService;
        _emailService = emailService;
        _calendarService = calendarService;
        _meetingService = meetingService;
        _openClawService = openClawService;
        _itemsTable = tableService.GetTableClient("Items");
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

        // Process with agent info (stores in Items table)
        await _emailService.Process(message, notification.ChangeType, webhook.AgentName, ct);

        // Notify agent via OpenClaw
        var task = FormatEmailTask(message, notification.ChangeType);
        await _openClawService.SpawnSession(webhook.AgentName, webhook.Source, task, ct);

        return true;
    }

    private static string FormatEmailTask(Microsoft.Graph.Models.Message message, string changeType)
    {
        var direction = message.From?.EmailAddress?.Address?.Contains("@muneris.") == true 
            ? "outbound" : "inbound";
        var bodyPreview = message.BodyPreview?.Length > 500 
            ? message.BodyPreview[..500] + "..." 
            : message.BodyPreview;

        return $"""
            Process {direction} email ({changeType}):
            From: {message.From?.EmailAddress?.Address}
            To: {string.Join(", ", (message.ToRecipients ?? []).Select(r => r.EmailAddress?.Address))}
            Subject: {message.Subject}
            Received: {message.ReceivedDateTime}

            Preview:
            {bodyPreview}
            """;
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

        // Process with agent info (stores in Items table)
        await _calendarService.Process(calendarEvent, notification.ChangeType, webhook.AgentName, ct);

        // Notify agent via OpenClaw
        var task = FormatCalendarTask(calendarEvent, notification.ChangeType);
        await _openClawService.SpawnSession(webhook.AgentName, webhook.Source, task, ct);

        return true;
    }

    private static string FormatCalendarTask(Microsoft.Graph.Models.Event evt, string changeType)
    {
        var attendees = string.Join(", ", (evt.Attendees ?? [])
            .Select(a => a.EmailAddress?.Address));

        return $"""
            Process calendar event ({changeType}):
            Subject: {evt.Subject}
            Organizer: {evt.Organizer?.EmailAddress?.Address}
            Start: {evt.Start?.DateTime}
            End: {evt.End?.DateTime}
            Location: {evt.Location?.DisplayName}
            Attendees: {attendees}

            Body:
            {evt.BodyPreview}
            """;
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

        // Process with agent info
        await _meetingService.Process(meeting, webhook.AgentName, ct);
        return true;
    }

    private async Task<bool> ProcessGenericWebhook(WebhookMessage webhook, CancellationToken ct)
    {
        // Store raw webhook data in Items table
        var rowKey = $"{webhook.Source}-{webhook.Type}-{Guid.NewGuid():N}";
        var rawData = webhook.NotificationData.GetRawText();
        
        var entity = new TableEntity(webhook.Type, rowKey)
        {
            { "AgentName", webhook.AgentName },
            { "SourceType", $"{webhook.Source}-{webhook.Type}" },
            { "Source", webhook.Source },
            { "RawData", rawData },
            { "WebhookUrl", webhook.WebhookUrl },
            { "ReceivedAt", webhook.ReceivedAt },
            { "IngestedAt", DateTimeOffset.UtcNow }
        };

        await _itemsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Stored {Source}/{Type} webhook for {Agent}: {RowKey}",
            webhook.Source, webhook.Type, webhook.AgentName, rowKey);

        // Notify agent via OpenClaw
        var task = $"""
            Process {webhook.Source} {webhook.Type} notification:
            
            Raw data:
            {rawData}
            """;
        await _openClawService.SpawnSession(webhook.AgentName, webhook.Source, task, ct);

        return true;
    }
}
