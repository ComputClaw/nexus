using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

public sealed class WhitelistFunction
{
    private readonly WhitelistService _whitelistService;
    private readonly string _apiKey;
    private readonly ILogger<WhitelistFunction> _logger;

    public WhitelistFunction(
        WhitelistService whitelistService,
        IConfiguration config,
        ILogger<WhitelistFunction> logger)
    {
        _whitelistService = whitelistService;
        _apiKey = config["IngestApiKey"] ?? throw new InvalidOperationException("IngestApiKey not configured");
        _logger = logger;
    }

    [Function("WhitelistList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "whitelist")]
        HttpRequestData req,
        CancellationToken ct)
    {
        if (!ValidateApiKey(req))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var domains = await _whitelistService.ListAll(ct);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(domains, ct);
        return response;
    }

    [Function("WhitelistAdd")]
    public async Task<HttpResponseData> Add(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "whitelist")]
        HttpRequestData req,
        CancellationToken ct)
    {
        if (!ValidateApiKey(req))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var body = await req.ReadFromJsonAsync<WhitelistRequest>(ct);
        if (body?.Domains == null || body.Domains.Count == 0)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Body must contain a 'domains' array", ct);
            return bad;
        }

        await _whitelistService.AddDomains(body.Domains, addedBy: "manual", ct);
        _logger.LogInformation("Added {Count} domain(s) to whitelist: {Domains}",
            body.Domains.Count, string.Join(", ", body.Domains));

        return req.CreateResponse(HttpStatusCode.Created);
    }

    [Function("WhitelistRemove")]
    public async Task<HttpResponseData> Remove(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "whitelist/{domain}")]
        HttpRequestData req,
        string domain,
        CancellationToken ct)
    {
        if (!ValidateApiKey(req))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        await _whitelistService.RemoveDomain(domain, ct);
        _logger.LogInformation("Removed domain from whitelist: {Domain}", domain);

        return req.CreateResponse(HttpStatusCode.NoContent);
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
