using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Nexus.Ingest.Services;

/// <summary>
/// Calls OpenClaw gateway to spawn agent sessions for webhook processing.
/// Uses per-source API keys for authentication.
/// </summary>
public sealed class OpenClawService
{
    private readonly HttpClient _httpClient;
    private readonly string _gatewayUrl;
    private readonly Dictionary<string, string> _sourceApiKeys;
    private readonly ILogger<OpenClawService> _logger;

    public OpenClawService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<OpenClawService> logger)
    {
        _httpClient = httpClient;
        _gatewayUrl = config["OpenClaw:GatewayUrl"] ?? "http://localhost:18789";
        _logger = logger;

        // Load per-source API keys
        _sourceApiKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        var graphKey = config["OpenClaw:ApiKeys:Graph"];
        if (!string.IsNullOrEmpty(graphKey))
            _sourceApiKeys["graph"] = graphKey;

        var putioKey = config["OpenClaw:ApiKeys:Putio"];
        if (!string.IsNullOrEmpty(putioKey))
            _sourceApiKeys["putio"] = putioKey;

        var firefliesKey = config["OpenClaw:ApiKeys:Fireflies"];
        if (!string.IsNullOrEmpty(firefliesKey))
            _sourceApiKeys["fireflies"] = firefliesKey;
    }

    /// <summary>
    /// Spawn an agent session to process webhook data.
    /// </summary>
    public async Task<bool> SpawnSession(
        string agentName,
        string source,
        string task,
        CancellationToken ct)
    {
        if (!_sourceApiKeys.TryGetValue(source, out var apiKey))
        {
            _logger.LogWarning("No API key configured for source: {Source}", source);
            return false;
        }

        var request = new
        {
            agentId = agentName,
            task = task,
            cleanup = "delete",
            runTimeoutSeconds = 120
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_gatewayUrl}/api/sessions/spawn");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = content;

        try
        {
            var response = await _httpClient.SendAsync(httpRequest, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Spawned session for {Agent} via {Source}: {Status}",
                    agentName, source, response.StatusCode);
                return true;
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Failed to spawn session for {Agent}: {Status} - {Body}",
                    agentName, response.StatusCode, body);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenClaw gateway for {Agent}", agentName);
            return false;
        }
    }
}
