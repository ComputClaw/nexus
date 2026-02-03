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

    public async Task Process(FirefliesTranscript transcript, CancellationToken ct)
    {
        // 1. Store full transcript in Blob Storage
        var blobPath = await _blobService.StoreTranscript(transcript, ct);

        // 2. Build Items entity
        var entity = MapMeetingToEntity(transcript, blobPath);
        await _itemsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation("Stored meeting: {Title} (transcript at {BlobPath})",
            transcript.Title, blobPath);

        // 3. Auto-whitelist participant domains
        var participantDomains = (transcript.MeetingAttendees ?? [])
            .Where(a => !string.IsNullOrEmpty(a.Email))
            .Select(a => ExtractDomain(a.Email!))
            .Distinct()
            .ToList();

        if (participantDomains.Count > 0)
        {
            var newDomains = await _whitelist.AddDomainsIfNew(participantDomains, "auto-meeting", ct);
            foreach (var domain in newDomains)
            {
                _logger.LogInformation("Auto-whitelisted domain from meeting: {Domain}", domain);
                await _whitelist.PromotePendingEmails(domain, ct);
            }
        }
    }

    private static TableEntity MapMeetingToEntity(FirefliesTranscript transcript, string blobPath)
    {
        var date = DateTimeOffset.TryParse(transcript.DateString, out var parsed)
            ? parsed : DateTimeOffset.UtcNow;

        return new TableEntity("meeting", transcript.Id)
        {
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
            { "IngestedAt", DateTimeOffset.UtcNow },
            { "SyncStatus", "pending" }
        };
    }

    private static string ExtractDomain(string email)
    {
        var at = email.IndexOf('@');
        return at >= 0 ? email[(at + 1)..].ToLowerInvariant() : "unknown";
    }
}
