using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Services;

/// <summary>
/// Unified ingestion service. Stores IngestionItems to Table Storage + blob references.
/// </summary>
public sealed class IngestionService
{
    private readonly TableClient _itemsTable;
    private readonly BlobStorageService _blobService;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(
        TableServiceClient tableService,
        BlobStorageService blobService,
        ILogger<IngestionService> logger)
    {
        _itemsTable = tableService.GetTableClient("Items");
        _blobService = blobService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        await _itemsTable.CreateIfNotExistsAsync();
    }

    /// <summary>
    /// Store a processed ingestion item: persist blobs, then upsert table entity.
    /// </summary>
    public async Task<string> StoreItem(IngestionItem item, CancellationToken ct)
    {
        var itemId = $"{item.SourceType}-{Guid.NewGuid():N}";

        // Store blobs and collect paths
        var blobPaths = new Dictionary<string, string>();
        if (item.Blobs?.Count > 0)
        {
            foreach (var (key, blob) in item.Blobs)
            {
                var path = await _blobService.StoreTextContent(
                    blob.Content,
                    blob.ContainerName,
                    blob.Prefix,
                    blob.Extension,
                    ct);
                blobPaths[key] = path;
            }
        }

        var entity = new TableEntity(item.SourceType, itemId)
        {
            { "AgentName", item.AgentName },
            { "SourceType", item.SourceType },
            { "Payload", JsonSerializer.Serialize(item.Payload) },
            { "ReceivedAt", item.ReceivedAt },
            { "IngestedAt", DateTimeOffset.UtcNow }
        };

        foreach (var (key, path) in blobPaths)
        {
            entity[key] = path;
        }

        await _itemsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Stored {SourceType} item for {Agent}: {ItemId}",
            item.SourceType, item.AgentName, itemId);

        return itemId;
    }

    /// <summary>
    /// Store any webhook payload with unified schema (used by AtomFeedPoller).
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
}
