using System.Net.Http.Json;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Nexus.Ingest.Graph;

/// <summary>
/// Manages Graph API subscriptions via REST (not SDK — avoids SDK serialization quirks).
/// Tracks active subscriptions in Azure Table Storage for renewal.
/// </summary>
public sealed class SubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly ClientSecretCredential _credential;
    private readonly TableClient _subscriptionTable;
    private readonly IConfiguration _config;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        TableServiceClient tableService,
        IConfiguration config,
        ILogger<SubscriptionService> logger)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") };
        _credential = new ClientSecretCredential(
            config["Graph:TenantId"],
            config["Graph:ClientId"],
            config["Graph:ClientSecret"]);
        _subscriptionTable = tableService.GetTableClient("Subscriptions");
        _config = config;
        _logger = logger;
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        var token = await _credential.GetTokenAsync(
            new Azure.Core.TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }), ct);
        return token.Token;
    }

    /// <summary>
    /// Build notification URL with URL-encoded function key.
    /// Reads base URL and function key separately to avoid encoding issues.
    /// </summary>
    private string BuildNotificationUrl(string route)
    {
        // Use config so dev/staging/prod can differ.
        // Examples:
        //   Nexus:PublicBaseUrl = https://nexusassistant.azurewebsites.net
        //   PublicBaseUrl       = https://nexusassistant.azurewebsites.net (legacy/shortcut)
        var publicBaseUrl = _config["Nexus:PublicBaseUrl"]
            ?? _config["PublicBaseUrl"]
            ?? "https://nexusassistant.azurewebsites.net";

        publicBaseUrl = publicBaseUrl.TrimEnd('/');
        var baseUrl = $"{publicBaseUrl}/api/{route}";

        // Function key is optional locally (depending on host), required in Azure.
        var functionKey = _config["FunctionKey"];
        if (!string.IsNullOrEmpty(functionKey))
        {
            var encodedKey = Uri.EscapeDataString(functionKey);
            return $"{baseUrl}?code={encodedKey}";
        }

        return baseUrl;
    }

    public async Task<string> Create(string resource, string changeTypes, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);

        var notificationUrl = BuildNotificationUrl("notifications");
        var lifecycleUrl = BuildNotificationUrl("lifecycle");

        var payload = new Dictionary<string, object?>
        {
            ["changeType"] = changeTypes,
            ["notificationUrl"] = notificationUrl,
            ["lifecycleNotificationUrl"] = lifecycleUrl,
            ["resource"] = resource,
            ["expirationDateTime"] = DateTimeOffset.UtcNow.AddDays(6).ToString("o"),
            ["clientState"] = _config["Graph:ClientState"]
        };

        _logger.LogInformation("Creating subscription for {Resource} with URL {NotificationUrl}",
            resource, notificationUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, "subscriptions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        // Log the exact payload for debugging
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
        _logger.LogInformation("Subscription payload: {Payload}", payloadJson);

        var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Graph subscription creation failed: {Status} {Body}",
                response.StatusCode, body);
            throw new InvalidOperationException(
                $"Graph subscription creation failed: {response.StatusCode} — {body}");
        }

        var result = JsonSerializer.Deserialize<JsonElement>(body);
        var subscriptionId = result.GetProperty("id").GetString()!;
        var expiry = result.GetProperty("expirationDateTime").GetString();

        // Track in table for renewal
        var entity = new TableEntity("subscription", subscriptionId)
        {
            { "Resource", resource },
            { "ChangeType", changeTypes },
            { "ExpiresAt", DateTimeOffset.Parse(expiry!) },
            { "CreatedAt", DateTimeOffset.UtcNow }
        };
        await _subscriptionTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogInformation(
            "Created Graph subscription {Id} for {Resource} (expires {Expiry})",
            subscriptionId, resource, expiry);

        return subscriptionId;
    }

    public async Task Renew(string subscriptionId, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        var newExpiry = DateTimeOffset.UtcNow.AddDays(6);

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"subscriptions/{subscriptionId}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { expirationDateTime = newExpiry.ToString("o") });

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Failed to renew subscription {Id}: {Status} {Body}",
                subscriptionId, response.StatusCode, body);
            throw new InvalidOperationException($"Renewal failed: {response.StatusCode}");
        }

        // Update tracking table
        try
        {
            var tableResponse = await _subscriptionTable.GetEntityAsync<TableEntity>(
                "subscription", subscriptionId, cancellationToken: ct);
            var entity = tableResponse.Value;
            entity["ExpiresAt"] = newExpiry;
            await _subscriptionTable.UpdateEntityAsync(entity, entity.ETag, cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Subscription {Id} not found in tracking table during renewal", subscriptionId);
        }

        _logger.LogInformation("Renewed subscription {Id} until {Expiry}", subscriptionId, newExpiry);
    }

    public async Task Reauthorize(string subscriptionId, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"subscriptions/{subscriptionId}/reauthorize");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent("");

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Reauthorize failed for {Id}: {Status}", subscriptionId, response.StatusCode);
        }
        else
        {
            _logger.LogInformation("Reauthorized subscription {Id}", subscriptionId);
        }
    }

    public async Task Recreate(string subscriptionId, CancellationToken ct)
    {
        try
        {
            var response = await _subscriptionTable.GetEntityAsync<TableEntity>(
                "subscription", subscriptionId, cancellationToken: ct);
            var entity = response.Value;
            var resource = entity.GetString("Resource");
            var changeTypes = entity.GetString("ChangeType");

            if (string.IsNullOrEmpty(resource) || string.IsNullOrEmpty(changeTypes))
            {
                _logger.LogError("Cannot recreate subscription {Id}: missing resource/changeTypes", subscriptionId);
                return;
            }

            await _subscriptionTable.DeleteEntityAsync("subscription", subscriptionId, cancellationToken: ct);
            await Create(resource, changeTypes, ct);
            _logger.LogInformation("Recreated subscription for {Resource}", resource);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogError("Cannot recreate subscription {Id}: not found in tracking table", subscriptionId);
        }
    }

    public async Task<List<TableEntity>> GetActiveSubscriptions(CancellationToken ct)
    {
        var results = new List<TableEntity>();
        await foreach (var entity in _subscriptionTable.QueryAsync<TableEntity>(
            filter: "PartitionKey eq 'subscription'", cancellationToken: ct))
        {
            results.Add(entity);
        }
        return results;
    }
}
