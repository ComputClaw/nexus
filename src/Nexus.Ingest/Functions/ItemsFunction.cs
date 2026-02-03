using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Read API for the sync consumer. Lists items, fetches blob content, deletes after sync.
/// Everything in the Items table is pending by definition — delete after processing.
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
    /// List all items (everything in the table is pending).
    /// Query params: type (email|calendar|meeting), top (default 100, max 500).
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
        var top = int.TryParse(query["top"], out var t) ? Math.Min(t, 500) : 100;

        // Filter by partition key (type) if specified
        var filter = !string.IsNullOrEmpty(type)
            ? $"PartitionKey eq '{type}'"
            : null;

        var items = new List<Dictionary<string, object?>>();
        var count = 0;

        await foreach (var entity in _itemsTable.QueryAsync<TableEntity>(
            filter: filter, maxPerPage: top, cancellationToken: ct))
        {
            if (count >= top) break;
            items.Add(EntityToDict(entity));
            count++;
        }

        _logger.LogInformation("Listed {Count} items (type={Type})", count, type ?? "all");

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
    /// Delete a single item after the consumer has processed it.
    /// Query params: type (partitionKey), id (rowKey).
    /// Idempotent — returns 204 even if already deleted.
    /// </summary>
    [Function("ItemsDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "items")]
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
            await _itemsTable.DeleteEntityAsync(type, id, cancellationToken: ct);
            _logger.LogInformation("Deleted item: {Type}/{Id}", type, id);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone — idempotent
            _logger.LogDebug("Item already deleted: {Type}/{Id}", type, id);
        }

        return req.CreateResponse(HttpStatusCode.NoContent);
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
