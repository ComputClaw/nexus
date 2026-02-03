using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using Nexus.Ingest.Helpers;

namespace Nexus.Ingest.Services;

/// <summary>
/// Processes calendar events: extracts data, writes to Items table.
/// Calendar = upsert (always save current state). Only flag cancelled on delete.
/// </summary>
public sealed class CalendarIngestionService
{
    private readonly TableClient _itemsTable;
    private readonly WhitelistService _whitelist;
    private readonly ILogger<CalendarIngestionService> _logger;

    public CalendarIngestionService(
        TableServiceClient tableService,
        WhitelistService whitelist,
        ILogger<CalendarIngestionService> logger)
    {
        _itemsTable = tableService.GetTableClient("Items");
        _whitelist = whitelist;
        _logger = logger;
    }

    public async Task Process(Event calendarEvent, string changeType, CancellationToken ct)
    {
        var action = changeType switch
        {
            "created" => "created",
            "updated" => "updated",
            "deleted" => "cancelled",
            _ => "created"
        };

        var entity = MapCalendarEventToEntity(calendarEvent, action);
        await _itemsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Stored calendar event ({Action}): {Subject} at {Start}",
            action, calendarEvent.Subject, calendarEvent.Start?.DateTime);

        // Auto-whitelist attendee email addresses (not domains)
        var attendeeEmails = (calendarEvent.Attendees ?? [])
            .Where(a => !string.IsNullOrEmpty(a.EmailAddress?.Address))
            .Select(a => a.EmailAddress!.Address!.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (attendeeEmails.Count > 0)
        {
            var newEmails = await _whitelist.AddEmailsIfNew(attendeeEmails, "auto-calendar", ct);
            foreach (var email in newEmails)
            {
                _logger.LogInformation("Auto-whitelisted email from calendar event: {Email}", email);
                await _whitelist.PromotePendingByEmail(email, ct);
            }
        }
    }

    public async Task ProcessDeletion(string resourceId, CancellationToken ct)
    {
        var sourceId = SanitizeRowKey(resourceId);
        var entity = new TableEntity("calendar", sourceId)
        {
            { "Action", "cancelled" },
            { "Source", "graph-calendar" },
            { "SourceId", resourceId },
            { "IngestedAt", DateTimeOffset.UtcNow },
            { "SyncStatus", "pending" }
        };
        await _itemsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation("Recorded calendar event cancellation: {Id}", resourceId);
    }

    private static TableEntity MapCalendarEventToEntity(Event evt, string action)
    {
        var startTime = evt.Start?.DateTime != null
            ? DateTimeOffset.Parse(evt.Start.DateTime) : (DateTimeOffset?)null;
        var endTime = evt.End?.DateTime != null
            ? DateTimeOffset.Parse(evt.End.DateTime) : (DateTimeOffset?)null;
        var sourceId = SanitizeRowKey(evt.Id ?? "unknown");

        return new TableEntity("calendar", sourceId)
        {
            { "FileName", FileNameBuilder.ForCalendarEvent(evt) },
            { "Action", action },
            { "Source", "graph-calendar" },
            { "SourceId", evt.Id },
            { "Subject", evt.Subject },
            { "From", evt.Organizer?.EmailAddress?.Address },
            { "Participants", JsonSerializer.Serialize((evt.Attendees ?? []).Select(a => new
            {
                name = a.EmailAddress?.Name,
                email = a.EmailAddress?.Address,
                status = a.Status?.Response?.ToString()
            })) },
            { "BodyText", HtmlStripper.StripHtml(evt.Body?.Content) },
            { "StartTime", startTime },
            { "EndTime", endTime },
            { "Location", evt.Location?.DisplayName ?? evt.OnlineMeetingUrl },
            { "ReceivedAt", startTime ?? DateTimeOffset.UtcNow },
            { "IngestedAt", DateTimeOffset.UtcNow },
            { "SyncStatus", "pending" }
        };
    }

    private static string ExtractDomain(string email)
    {
        var at = email.IndexOf('@');
        return at >= 0 ? email[(at + 1)..].ToLowerInvariant() : "unknown";
    }

    private static string SanitizeRowKey(string id)
    {
        return id
            .Replace("/", "")
            .Replace("\\", "")
            .Replace("#", "")
            .Replace("?", "");
    }
}
