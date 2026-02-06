using System.Net;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Stores raw OpenClaw session transcripts in Blob Storage.
/// Auth: Function key (Azure).
/// </summary>
public sealed class SessionsFunction
{
    private const int MaxTranscriptBytes = 10_485_760; // 10 MB (blob can handle it)
    private const string ContainerName = "sessions";

    private readonly BlobContainerClient _container;
    private readonly ILogger<SessionsFunction> _logger;

    public SessionsFunction(
        BlobServiceClient blobService,
        ILogger<SessionsFunction> logger)
    {
        _container = blobService.GetBlobContainerClient(ContainerName);
        _logger = logger;
    }

    [Function("SessionsPost")]
    public async Task<HttpResponseData> Post(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "sessions")]
        HttpRequestData req,
        CancellationToken ct)
    {
        // Check Content-Length header first to fail fast on oversized payloads
        if (req.Headers.TryGetValues("Content-Length", out var contentLengthValues))
        {
            var contentLengthStr = contentLengthValues.FirstOrDefault();
            if (long.TryParse(contentLengthStr, out var contentLength) && contentLength > MaxTranscriptBytes * 2)
            {
                _logger.LogWarning("Request body too large: {ContentLength} bytes", contentLength);
                var large = req.CreateResponse(HttpStatusCode.RequestEntityTooLarge);
                await large.WriteAsJsonAsync(new { error = $"Request body too large ({contentLength} bytes). Maximum is {MaxTranscriptBytes} bytes." }, ct);
                return large;
            }
        }

        SessionRequest? body;
        try
        {
            body = await req.ReadFromJsonAsync<SessionRequest>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize request body");
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid JSON or request body too large" }, ct);
            return bad;
        }

        if (body is null
            || string.IsNullOrWhiteSpace(body.AgentId)
            || string.IsNullOrWhiteSpace(body.SessionId)
            || string.IsNullOrWhiteSpace(body.Transcript))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Missing required fields: agentId, sessionId, transcript" }, ct);
            return bad;
        }

        // Validate sessionId is a valid GUID (36 chars)
        if (body.SessionId.Length != 36 || !Guid.TryParse(body.SessionId, out _))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid sessionId format — expected UUID" }, ct);
            return bad;
        }

        // Validate transcript size
        var transcriptBytes = Encoding.UTF8.GetByteCount(body.Transcript);
        if (transcriptBytes > MaxTranscriptBytes)
        {
            var large = req.CreateResponse(HttpStatusCode.RequestEntityTooLarge);
            await large.WriteAsJsonAsync(new { error = $"Transcript exceeds {MaxTranscriptBytes / 1024 / 1024} MB limit" }, ct);
            return large;
        }

        // Ensure container exists
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);

        // Upload to blob: sessions/{agentId}/{sessionId}.jsonl
        var blobPath = $"{body.AgentId}/{body.SessionId}.jsonl";
        var blobClient = _container.GetBlobClient(blobPath);

        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body.Transcript));
            await blobClient.UploadAsync(stream, overwrite: false, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogWarning("Duplicate session: {SessionId}", body.SessionId);
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(new { error = "Session already exists", sessionId = body.SessionId }, ct);
            return conflict;
        }

        _logger.LogInformation("Stored session {SessionId} for agent {AgentId} ({Bytes} bytes)",
            body.SessionId, body.AgentId, transcriptBytes);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            status = "ok",
            sessionId = body.SessionId,
            path = blobPath,
            bytes = transcriptBytes,
            stored = DateTime.UtcNow
        }, ct);
        return response;
    }
}
