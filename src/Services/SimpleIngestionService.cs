using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Services;

/// <summary>
/// Simplified ingestion service with unified Items table schema.
/// Stores full payload as JSON + blob references for large content.
/// </summary>
public sealed class SimpleIngestionService
{
    private readonly TableClient _itemsTable;
    private readonly BlobStorageService _blobService;
    private readonly ILogger<SimpleIngestionService> _logger;

    public SimpleIngestionService(
        TableServiceClient tableService,
        BlobStorageService blobService,
        ILogger<SimpleIngestionService> logger)
    {
        _itemsTable = tableService.GetTableClient("Items");
        _blobService = blobService;
        _logger = logger;
    }

    /// <summary>
    /// Store any webhook payload with unified schema.
    /// </summary>
    public async Task<string> StoreItem<T>(
        T payload,
        string sourceType,
        string agentName,
        DateTimeOffset? receivedAt = null,
        Dictionary<string, string>? blobs = null,
        CancellationToken ct = default)
    {
        var itemId = $"{sourceType}-{Guid.NewGuid():N}";
        var received = receivedAt ?? DateTimeOffset.UtcNow;

        var entity = new TableEntity(sourceType, itemId)
        {
            { "AgentName", agentName },
            { "SourceType", sourceType },
            { "Payload", JsonSerializer.Serialize(payload) },
            { "ReceivedAt", received },
            { "IngestedAt", DateTimeOffset.UtcNow }
        };

        // Add blob references if provided
        if (blobs?.Count > 0)
        {
            foreach (var (key, blobPath) in blobs)
            {
                entity[key] = blobPath;
            }
        }

        await _itemsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Stored {SourceType} item for {Agent}: {ItemId}",
            sourceType, agentName, itemId);

        return itemId;
    }

    /// <summary>
    /// Store email with body blob.
    /// </summary>
    public async Task<string> StoreEmail(
        object emailPayload,
        string agentName,
        string? bodyContent = null,
        List<string>? attachmentPaths = null,
        DateTimeOffset? receivedAt = null,
        CancellationToken ct = default)
    {
        var blobs = new Dictionary<string, string>();

        // Store body content as blob if provided
        if (!string.IsNullOrEmpty(bodyContent))
        {
            var bodyBlobPath = await _blobService.StoreTextContent(
                bodyContent,
                "emails",
                "body",
                "html",
                ct);
            blobs["BodyBlob"] = bodyBlobPath;
        }

        // Store attachment paths if provided
        if (attachmentPaths?.Count > 0)
        {
            blobs["AttachmentBlobs"] = JsonSerializer.Serialize(attachmentPaths);
        }

        return await StoreItem(
            emailPayload,
            "graph-email",
            agentName,
            receivedAt,
            blobs,
            ct);
    }

    /// <summary>
    /// Store meeting with transcript blob.
    /// </summary>
    public async Task<string> StoreMeeting(
        object meetingPayload,
        string agentName,
        string? transcriptContent = null,
        DateTimeOffset? receivedAt = null,
        CancellationToken ct = default)
    {
        var blobs = new Dictionary<string, string>();

        // Store transcript as blob if provided
        if (!string.IsNullOrEmpty(transcriptContent))
        {
            var transcriptBlobPath = await _blobService.StoreTextContent(
                transcriptContent,
                "meetings",
                "transcript",
                "txt",
                ct);
            blobs["TranscriptBlob"] = transcriptBlobPath;
        }

        return await StoreItem(
            meetingPayload,
            "fireflies-meeting",
            agentName,
            receivedAt,
            blobs,
            ct);
    }

    /// <summary>
    /// Store GitHub release (no blobs needed).
    /// </summary>
    public async Task<string> StoreGitHubRelease(
        object releasePayload,
        string agentName,
        DateTimeOffset? receivedAt = null,
        CancellationToken ct = default)
    {
        return await StoreItem(
            releasePayload,
            "github-release",
            agentName,
            receivedAt,
            null,
            ct);
    }

    /// <summary>
    /// Store generic webhook payload.
    /// </summary>
    public async Task<string> StoreGeneric(
        object payload,
        string source,
        string type,
        string agentName,
        DateTimeOffset? receivedAt = null,
        CancellationToken ct = default)
    {
        var sourceType = $"{source}-{type}";
        return await StoreItem(
            payload,
            sourceType,
            agentName,
            receivedAt,
            null,
            ct);
    }
}