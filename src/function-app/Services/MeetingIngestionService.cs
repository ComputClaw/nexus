using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Helpers;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Services;

/// <summary>
/// Processes meeting transcripts: stores in blob, writes metadata to Items, auto-whitelists participants.
/// </summary>
public sealed class MeetingIngestionService
{
    private readonly TableClient _itemsTable;
    private readonly WhitelistService _whitelist;
    private readonly BlobStorageService _blobService;
    private readonly ILogger<MeetingIngestionService> _logger;

    public MeetingIngestionService(
        TableServiceClient tableService,
        WhitelistService whitelist,
        BlobStorageService blobService,
        ILogger<MeetingIngestionService> logger)
    {
        _itemsTable = tableService.GetTableClient("Items");
        _whitelist = whitelist;
        _blobService = blobService;
        _logger = logger;
    }

    public async Task Process(FirefliesTranscript transcript, string agentName, CancellationToken ct)
    {
        // 1. Store full transcript in Blob Storage
        var blobPath = await _blobService.StoreTranscript(transcript, ct);

        // 2. Build Items entity with agent routing info
        var entity = MapMeetingToEntity(transcript, blobPath, agentName);
        await _itemsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation("Stored meeting for {Agent}: {Title} (transcript at {BlobPath})",
            agentName, transcript.Title, blobPath);

        // 3. Auto-whitelist participant email addresses (not domains)
        var participantEmails = (transcript.MeetingAttendees ?? [])
            .Where(a => !string.IsNullOrEmpty(a.Email))
            .Select(a => a.Email!.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (participantEmails.Count > 0)
        {
            var newEmails = await _whitelist.AddEmailsIfNew(participantEmails, "auto-meeting", ct);
            foreach (var email in newEmails)
            {
                _logger.LogInformation("Auto-whitelisted email from meeting: {Email}", email);
                await _whitelist.PromotePendingByEmail(email, ct);
            }
        }
    }

    private static TableEntity MapMeetingToEntity(FirefliesTranscript transcript, string blobPath, string agentName)
    {
        var date = DateTimeOffset.TryParse(transcript.DateString, out var parsed)
            ? parsed : DateTimeOffset.UtcNow;

        return new TableEntity("meeting", transcript.Id)
        {
            // Agent routing info
            { "AgentName", agentName },
            { "SourceType", "fireflies-meeting" },
            // Standard fields
            { "FileName", FileNameBuilder.ForMeeting(transcript) },
            { "Action", "created" },
            { "Source", "fireflies" },
            { "SourceId", transcript.Id },
            { "Subject", transcript.Title },
            { "From", transcript.OrganizerEmail },
            { "Participants", JsonSerializer.Serialize((transcript.MeetingAttendees ?? []).Select(a => new
            {
                name = a.DisplayName,
                email = a.Email
            })) },
            { "Summary", transcript.Summary?.Overview },
            { "ActionItems", transcript.Summary?.ActionItems },
            { "TranscriptBlob", blobPath },
            { "StartTime", date },
            { "EndTime", date.AddMinutes(transcript.Duration ?? 0) },
            { "ReceivedAt", date },
            { "IngestedAt", DateTimeOffset.UtcNow }
        };
    }

    private static string ExtractDomain(string email)
    {
        var at = email.IndexOf('@');
        return at >= 0 ? email[(at + 1)..].ToLowerInvariant() : "unknown";
    }
}
