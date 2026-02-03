using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Read API for the sync consumer. Lists items and fetches blob content.
/// Auth: Function key (Azure) + X-Api-Key (application).
/// </summary>
public sealed class ItemsFunction
{
    private readonly TableClient _itemsTable;
    private readonly BlobStorageService _blobService;
    private readonly string _apiKey;
    private readonly ILogger<ItemsFunction> _logger;

    public ItemsFunction(
        TableServiceClient tableService,
        BlobStorageService blobService,
        IConfiguration config,
        ILogger<ItemsFunction> logger)
    {
        _itemsTable = tableService.GetTableClient("Items");
        _blobService = blobService;
        _apiKey = config["IngestApiKey"] ?? throw new InvalidOperationException("IngestApiKey not configured");
        _logger = logger;
    }

    /// <summary>
    /// List items with optional filters.
    /// Query params: type (email|calendar|meeting), status (pending|synced), top (default 100).
    /// </summary>
    [Function("ItemsList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "items")]
        HttpRequestData req,
        CancellationToken ct)
    {
        if (!ValidateApiKey(req))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var type = query["type"];
        var status = query["status"] ?? "pending";
        var top = int.TryParse(query["top"], out var t) ? Math.Min(t, 500) : 100;

        // Build OData filter
        var filters = new List<string>();
        if (!string.IsNullOrEmpty(type))
            filters.Add($"PartitionKey eq '{type}'");
        if (!string.IsNullOrEmpty(status))
            filters.Add($"SyncStatus eq '{status}'");

        var filter = filters.Count > 0 ? string.Join(" and ", filters) : null;

        var items = new List<Dictionary<string, object?>>();
        var count = 0;

        await foreach (var entity in _itemsTable.QueryAsync<TableEntity>(
            filter: filter, maxPerPage: top, cancellationToken: ct))
        {
            if (count >= top) break;
            items.Add(EntityToDict(entity));
            count++;
        }

        _logger.LogInformation("Listed {Count} items (type={Type}, status={Status})", count, type, status);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { items, count }, ct);
        return response;
    }

    /// <summary>
    /// Get full body/transcript content from blob storage.
    /// Query params: type (email|calendar|meeting), id (rowKey).
    /// Falls back to BodyText from table if no blob exists.
    /// </summary>
    [Function("ItemsGetBody")]
    public async Task<HttpResponseData> GetBody(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "items/body")]
        HttpRequestData req,
        CancellationToken ct)
    {
        if (!ValidateApiKey(req))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var type = query["type"];
        var id = query["id"];

        if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Required query params: type, id", ct);
            return bad;
        }

        try
        {
            var entity = await _itemsTable.GetEntityAsync<TableEntity>(
                type, id, cancellationToken: ct);

            // Determine blob path based on type
            string? blobPath = type switch
            {
                "email" => entity.Value.GetString("FullBodyBlob"),
                "meeting" => entity.Value.GetString("TranscriptBlob"),
                _ => null
            };

            if (string.IsNullOrEmpty(blobPath))
            {
                // No blob — return BodyText from the table entity
                var bodyText = entity.Value.GetString("BodyText") ?? "";
                var textResponse = req.CreateResponse(HttpStatusCode.OK);
                textResponse.Headers.Add("Content-Type", "text/plain; charset=utf-8");
                await textResponse.WriteStringAsync(bodyText, ct);
                return textResponse;
            }

            // Fetch from blob
            var content = await _blobService.ReadBlob(type, blobPath, ct);
            if (content == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            await response.WriteStringAsync(content, ct);
            return response;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }
    }

    /// <summary>
    /// Mark items as synced. Accepts array of {partitionKey, rowKey} pairs.
    /// Sets SyncStatus = "synced" and adds SyncedAt timestamp.
    /// </summary>
    [Function("ItemsMarkSynced")]
    public async Task<HttpResponseData> MarkSynced(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "items/sync")]
        HttpRequestData req,
        CancellationToken ct)
    {
        if (!ValidateApiKey(req))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var body = await req.ReadFromJsonAsync<ItemsSyncRequest>(ct);
        if (body?.Items == null || body.Items.Count == 0)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Body must contain an 'items' array", ct);
            return bad;
        }

        var synced = 0;
        foreach (var item in body.Items)
        {
            try
            {
                var entity = await _itemsTable.GetEntityAsync<TableEntity>(
                    item.PartitionKey, item.RowKey, cancellationToken: ct);

                entity.Value["SyncStatus"] = "synced";
                entity.Value["SyncedAt"] = DateTimeOffset.UtcNow;

                await _itemsTable.UpdateEntityAsync(
                    entity.Value, entity.Value.ETag, cancellationToken: ct);
                synced++;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("Item not found for sync: {PK}/{RK}",
                    item.PartitionKey, item.RowKey);
            }
        }

        _logger.LogInformation("Marked {Synced}/{Total} items as synced", synced, body.Items.Count);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { synced, total = body.Items.Count }, ct);
        return response;
    }

    private static Dictionary<string, object?> EntityToDict(TableEntity entity)
    {
        var dict = new Dictionary<string, object?>
        {
            ["partitionKey"] = entity.PartitionKey,
            ["rowKey"] = entity.RowKey,
            ["timestamp"] = entity.Timestamp
        };

        foreach (var prop in entity)
        {
            if (prop.Key is "odata.etag" or "PartitionKey" or "RowKey" or "Timestamp")
                continue;
            dict[prop.Key] = prop.Value;
        }

        return dict;
    }

    private bool ValidateApiKey(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("X-Api-Key", out var values))
        {
            return values.FirstOrDefault() == _apiKey;
        }
        return false;
    }
}
