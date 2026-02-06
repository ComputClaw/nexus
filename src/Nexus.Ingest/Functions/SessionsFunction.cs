using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Stores raw OpenClaw session transcripts for analytics and archival.
/// Auth: Function key (Azure).
/// </summary>
public sealed class SessionsFunction
{
    private const int MaxTranscriptBytes = 1_048_576; // 1 MB

    private readonly TableClient _sessionsTable;
    private readonly ILogger<SessionsFunction> _logger;

    public SessionsFunction(
        TableServiceClient tableService,
        ILogger<SessionsFunction> logger)
    {
        _sessionsTable = tableService.GetTableClient("Sessions");
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
                await large.WriteAsJsonAsync(new { error = $"Request body too large ({contentLength} bytes). Maximum transcript size is {MaxTranscriptBytes} bytes." }, ct);
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

        // Validate transcript size ≤ 1 MB
        if (System.Text.Encoding.UTF8.GetByteCount(body.Transcript) > MaxTranscriptBytes)
        {
            var large = req.CreateResponse(HttpStatusCode.RequestEntityTooLarge);
            await large.WriteAsJsonAsync(new { error = "Transcript exceeds 1 MB limit" }, ct);
            return large;
        }

        var now = DateTime.UtcNow;
        var entity = new TableEntity(now.ToString("yyyy-MM-dd"), body.SessionId)
        {
            { "AgentId", body.AgentId },
            { "RawData", body.Transcript }
        };

        try
        {
            await _sessionsTable.AddEntityAsync(entity, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogWarning("Duplicate session: {SessionId}", body.SessionId);
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(new { error = "Session already exists", sessionId = body.SessionId }, ct);
            return conflict;
        }

        _logger.LogInformation("Stored session {SessionId} for agent {AgentId}", body.SessionId, body.AgentId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { status = "ok", sessionId = body.SessionId, stored = now }, ct);
        return response;
    }
}
