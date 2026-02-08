using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// HTTP function for managing feed configurations.
/// Provides CRUD operations for Atom/RSS feed subscriptions.
/// </summary>
public sealed class FeedManagementFunction
{
    private readonly FeedManagementService _feedManagementService;
    private readonly ILogger<FeedManagementFunction> _logger;

    public FeedManagementFunction(
        FeedManagementService feedManagementService,
        ILogger<FeedManagementFunction> logger)
    {
        _feedManagementService = feedManagementService;
        _logger = logger;
    }

    [Function("FeedManagement")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", "delete", "patch",
            Route = "feeds/{id?}")]
        HttpRequestData req,
        string? id,
        CancellationToken ct)
    {
        try
        {
            return req.Method.ToUpperInvariant() switch
            {
                "GET" => await HandleGetAsync(req, id, ct),
                "POST" => await HandlePostAsync(req, ct),
                "DELETE" => await HandleDeleteAsync(req, id, ct),
                "PATCH" => await HandlePatchAsync(req, id, ct),
                _ => await CreateErrorResponse(req, HttpStatusCode.MethodNotAllowed, 
                    $"Method {req.Method} not allowed")
            };
        }
        catch (FeedValidationException ex)
        {
            _logger.LogWarning("Feed validation error: {Error}", ex.Message);
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (FeedManagementException ex)
        {
            _logger.LogError(ex, "Feed management error: {Error}", ex.Message);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, ex.Message);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Invalid JSON in request: {Error}", ex.Message);
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid JSON format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in feed management");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                "An unexpected error occurred");
        }
    }

    /// <summary>
    /// Handle GET requests - list all feeds or get specific feed.
    /// </summary>
    private async Task<HttpResponseData> HandleGetAsync(
        HttpRequestData req, 
        string? id, 
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            // GET /api/feeds - list all feeds
            _logger.LogInformation("Retrieving all feeds");
            
            var feeds = await _feedManagementService.GetAllFeedsAsync(ct);
            
            return await CreateJsonResponse(req, HttpStatusCode.OK, new
            {
                feeds = feeds,
                count = feeds.Count
            });
        }
        else
        {
            // GET /api/feeds/{id} - get specific feed
            _logger.LogInformation("Retrieving feed {FeedId}", id);
            
            var feed = await _feedManagementService.GetFeedAsync(id, ct);
            
            if (feed == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, 
                    $"Feed '{id}' not found");
            }
            
            return await CreateJsonResponse(req, HttpStatusCode.OK, feed);
        }
    }

    /// <summary>
    /// Handle POST requests - create new feed.
    /// </summary>
    private async Task<HttpResponseData> HandlePostAsync(HttpRequestData req, CancellationToken ct)
    {
        _logger.LogInformation("Creating new feed");
        
        var body = await req.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
        }

        var createRequest = JsonSerializer.Deserialize<CreateFeedRequest>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (createRequest == null)
        {
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid request format");
        }

        var feed = await _feedManagementService.CreateFeedAsync(createRequest, ct);
        
        _logger.LogInformation("Created feed {FeedId}", feed.Id);
        
        return await CreateJsonResponse(req, HttpStatusCode.Created, feed);
    }

    /// <summary>
    /// Handle DELETE requests - remove feed.
    /// </summary>
    private async Task<HttpResponseData> HandleDeleteAsync(
        HttpRequestData req, 
        string? id, 
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Feed ID is required");
        }

        _logger.LogInformation("Deleting feed {FeedId}", id);
        
        var deleted = await _feedManagementService.DeleteFeedAsync(id, ct);
        
        if (!deleted)
        {
            return await CreateErrorResponse(req, HttpStatusCode.NotFound, $"Feed '{id}' not found");
        }
        
        _logger.LogInformation("Deleted feed {FeedId}", id);
        
        return await CreateJsonResponse(req, HttpStatusCode.OK, new { message = $"Feed '{id}' deleted successfully" });
    }

    /// <summary>
    /// Handle PATCH requests - update feed properties.
    /// </summary>
    private async Task<HttpResponseData> HandlePatchAsync(
        HttpRequestData req, 
        string? id, 
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Feed ID is required");
        }

        var body = await req.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
        }

        // Parse patch request - for now, only support enabling/disabling feeds
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.TryGetProperty("enabled", out var enabledProperty) && 
            enabledProperty.ValueKind == JsonValueKind.True || enabledProperty.ValueKind == JsonValueKind.False)
        {
            var enabled = enabledProperty.GetBoolean();
            
            _logger.LogInformation("Setting feed {FeedId} enabled = {Enabled}", id, enabled);
            
            var feed = await _feedManagementService.SetFeedEnabledAsync(id, enabled, ct);
            
            if (feed == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, $"Feed '{id}' not found");
            }
            
            return await CreateJsonResponse(req, HttpStatusCode.OK, feed);
        }

        return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
            "Only 'enabled' property updates are currently supported");
    }

    /// <summary>
    /// Create a JSON response with the specified status code and data.
    /// </summary>
    private static async Task<HttpResponseData> CreateJsonResponse<T>(
        HttpRequestData req, 
        HttpStatusCode statusCode, 
        T data)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json");
        
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        
        await response.WriteStringAsync(json);
        return response;
    }

    /// <summary>
    /// Create an error response with the specified status code and message.
    /// </summary>
    private static async Task<HttpResponseData> CreateErrorResponse(
        HttpRequestData req, 
        HttpStatusCode statusCode, 
        string message)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json");
        
        var errorData = new
        {
            error = message,
            statusCode = (int)statusCode,
            timestamp = DateTimeOffset.UtcNow
        };
        
        var json = JsonSerializer.Serialize(errorData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        await response.WriteStringAsync(json);
        return response;
    }
}