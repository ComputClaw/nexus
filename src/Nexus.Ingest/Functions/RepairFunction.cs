using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Repair endpoints for fixing data issues.
/// </summary>
public sealed class RepairFunction
{
    private readonly TableClient _itemsTable;
    private readonly GraphService _graphService;
    private readonly BlobStorageService _blobService;
    private readonly ILogger<RepairFunction> _logger;

    public RepairFunction(
        TableServiceClient tableService,
        GraphService graphService,
        BlobStorageService blobService,
        ILogger<RepairFunction> logger)
    {
        _itemsTable = tableService.GetTableClient("Items");
        _graphService = graphService;
        _blobService = blobService;
        _logger = logger;
    }

    /// <summary>
    /// Re-fetch email bodies from Graph and update blob storage.
    /// Fixes corrupted blobs caused by ID truncation collision bug.
    /// Query params: top (default 100, max 500), dryRun (default false).
    /// </summary>
    [Function("RepairEmailBodies")]
    public async Task<HttpResponseData> RepairEmailBodies(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "repair/email-bodies")]
        HttpRequestData req,
        CancellationToken ct)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var top = int.TryParse(query["top"], out var t) ? Math.Min(t, 500) : 100;
        var dryRun = query["dryRun"]?.ToLowerInvariant() == "true";

        var results = new List<object>();
        var processed = 0;
        var failed = 0;

        await foreach (var entity in _itemsTable.QueryAsync<TableEntity>(
            filter: "PartitionKey eq 'email'", maxPerPage: top, cancellationToken: ct))
        {
            if (processed >= top) break;

            var sourceId = entity.GetString("SourceId");
            var subject = entity.GetString("Subject");

            if (string.IsNullOrEmpty(sourceId))
            {
                results.Add(new { rowKey = entity.RowKey, status = "skipped", reason = "no SourceId" });
                continue;
            }

            try
            {
                // Fetch fresh message from Graph
                var message = await _graphService.FetchMessage(sourceId, ct);
                if (message == null)
                {
                    results.Add(new { rowKey = entity.RowKey, subject, status = "failed", reason = "Graph fetch returned null" });
                    failed++;
                    continue;
                }

                if (dryRun)
                {
                    results.Add(new { rowKey = entity.RowKey, subject, status = "would_repair", bodyLength = message.Body?.Content?.Length ?? 0 });
                }
                else
                {
                    // Store body with new hash-based blob name
                    var newBlobPath = await _blobService.StoreEmailBody(message, ct);

                    // Update Items table with new blob path
                    entity["FullBodyBlob"] = newBlobPath;
                    await _itemsTable.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);

                    results.Add(new { rowKey = entity.RowKey, subject, status = "repaired", newBlobPath });
                    _logger.LogInformation("Repaired email body: {Subject} -> {BlobPath}", subject, newBlobPath);
                }

                processed++;
            }
            catch (Exception ex)
            {
                results.Add(new { rowKey = entity.RowKey, subject, status = "error", message = ex.Message });
                failed++;
                _logger.LogError(ex, "Failed to repair email body for {RowKey}", entity.RowKey);
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            dryRun,
            processed,
            failed,
            results
        }, ct);
        return response;
    }
}
