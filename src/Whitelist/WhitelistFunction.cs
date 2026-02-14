using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Whitelist;

/// <summary>
/// HTTP-triggered function that manages the sender whitelist for filtering inbound webhooks.
/// </summary>
public sealed class WhitelistFunction
{
    private readonly WhitelistService _whitelistService;
    private readonly ILogger<WhitelistFunction> _logger;

    public WhitelistFunction(
        WhitelistService whitelistService,
        ILogger<WhitelistFunction> logger)
    {
        _whitelistService = whitelistService;
        _logger = logger;
    }

    [Function("WhitelistList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "whitelist")]
        HttpRequestData req,
        CancellationToken ct)
    {
        var entries = await _whitelistService.ListAll(ct);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(entries, ct);
        return response;
    }

    [Function("WhitelistAdd")]
    public async Task<HttpResponseData> Add(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "whitelist")]
        HttpRequestData req,
        CancellationToken ct)
    {
        var body = await req.ReadFromJsonAsync<WhitelistRequest>(ct);
        if (body == null || (body.Domains.Count == 0 && body.Emails.Count == 0))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Body must contain a 'domains' and/or 'emails' array", ct);
            return bad;
        }

        // Add domains + promote pending emails from those domains
        if (body.Domains.Count > 0)
        {
            var newDomains = await _whitelistService.AddDomainsIfNew(body.Domains, "manual", ct);
            foreach (var domain in newDomains)
            {
                await _whitelistService.PromotePendingByDomain(domain, ct);
                _logger.LogInformation("Whitelisted domain and promoted pending: {Domain}", domain);
            }

            // Also re-promote for existing domains (in case pending emails arrived after initial whitelist)
            var existing = body.Domains.Except(newDomains, StringComparer.OrdinalIgnoreCase);
            foreach (var domain in existing)
            {
                await _whitelistService.PromotePendingByDomain(domain, ct);
            }
        }

        // Add emails + promote pending emails from those addresses
        if (body.Emails.Count > 0)
        {
            var newEmails = await _whitelistService.AddEmailsIfNew(body.Emails, "manual", ct);
            foreach (var email in newEmails)
            {
                await _whitelistService.PromotePendingByEmail(email, ct);
                _logger.LogInformation("Whitelisted email and promoted pending: {Email}", email);
            }

            // Also re-promote for existing emails
            var existing = body.Emails.Except(newEmails, StringComparer.OrdinalIgnoreCase);
            foreach (var email in existing)
            {
                await _whitelistService.PromotePendingByEmail(email, ct);
            }
        }

        _logger.LogInformation("Whitelist updated: {DomainCount} domain(s), {EmailCount} email(s)",
            body.Domains.Count, body.Emails.Count);

        return req.CreateResponse(HttpStatusCode.Created);
    }

    [Function("WhitelistRemove")]
    public async Task<HttpResponseData> Remove(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "whitelist/{type}/{value}")]
        HttpRequestData req,
        string type,
        string value,
        CancellationToken ct)
    {
        if (type.Equals("email", StringComparison.OrdinalIgnoreCase))
        {
            await _whitelistService.RemoveEmail(value, ct);
            _logger.LogInformation("Removed email from whitelist: {Email}", value);
        }
        else
        {
            await _whitelistService.RemoveDomain(value, ct);
            _logger.LogInformation("Removed domain from whitelist: {Domain}", value);
        }

        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}
