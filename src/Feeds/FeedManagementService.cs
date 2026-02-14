using System.Text.RegularExpressions;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Feeds;

/// <summary>
/// Service for managing feed configurations in Azure Table Storage.
/// Handles CRUD operations and validation for feed subscriptions.
/// </summary>
public sealed class FeedManagementService
{
    private readonly TableClient _feedConfigsTable;
    private readonly AtomFeedService _atomFeedService;
    private readonly HashSet<string> _validAgents;
    private readonly ILogger<FeedManagementService> _logger;

    // Valid agent names (should match WebhookRelayFunction)
    private static readonly HashSet<string> DefaultValidAgents = new(StringComparer.OrdinalIgnoreCase)
    {
        "stewardclaw", "sageclaw", "main", "flickclaw", "puzzlesclaw"
    };

    public FeedManagementService(
        TableServiceClient tableService,
        AtomFeedService atomFeedService,
        IConfiguration config,
        ILogger<FeedManagementService> logger)
    {
        _feedConfigsTable = tableService.GetTableClient("FeedConfigs");
        _atomFeedService = atomFeedService;
        _logger = logger;

        // Allow configuration override for valid agents
        var configuredAgents = config["ValidAgents"]?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        _validAgents = configuredAgents?.Length > 0
            ? new HashSet<string>(configuredAgents, StringComparer.OrdinalIgnoreCase)
            : DefaultValidAgents;

        _logger.LogInformation("FeedManagementService initialized with {AgentCount} valid agents: {Agents}",
            _validAgents.Count, string.Join(", ", _validAgents));
    }

    /// <summary>
    /// Initialize the service (ensure table exists).
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _feedConfigsTable.CreateIfNotExistsAsync(ct);
        _logger.LogInformation("FeedConfigs table initialized");
    }

    /// <summary>
    /// Create a new feed configuration.
    /// </summary>
    public async Task<FeedResponse> CreateFeedAsync(
        CreateFeedRequest request,
        CancellationToken ct = default)
    {
        // Validate the request
        var validationResult = await ValidateCreateRequest(request, ct);
        if (!validationResult.IsValid)
        {
            throw new FeedValidationException(validationResult.ErrorMessage!);
        }

        // Generate ID if not provided
        var feedId = !string.IsNullOrWhiteSpace(request.Id)
            ? SanitizeFeedId(request.Id)
            : GenerateFeedId(request.FeedUrl, request.SourceType);

        // Check for existing feed with same ID
        var existingFeed = await GetFeedByIdAsync(feedId, ct);
        if (existingFeed != null)
        {
            throw new FeedValidationException($"Feed with ID '{feedId}' already exists");
        }

        // Create the feed configuration
        var feedConfig = new FeedConfig(feedId, request.FeedUrl, request.AgentName, request.SourceType)
        {
            Enabled = request.Enabled
        };

        try
        {
            await _feedConfigsTable.UpsertEntityAsync(feedConfig, TableUpdateMode.Replace, ct);

            _logger.LogInformation(
                "Created feed {FeedId}: {FeedUrl} → {AgentName} ({SourceType})",
                feedId, request.FeedUrl, request.AgentName, request.SourceType);

            return FeedResponse.FromConfig(feedConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create feed {FeedId}", feedId);
            throw new FeedManagementException($"Failed to create feed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get all feed configurations.
    /// </summary>
    public async Task<List<FeedResponse>> GetAllFeedsAsync(CancellationToken ct = default)
    {
        try
        {
            var feeds = new List<FeedResponse>();

            await foreach (var entity in _feedConfigsTable.QueryAsync<FeedConfig>(
                filter: $"PartitionKey eq 'feed'",
                cancellationToken: ct))
            {
                feeds.Add(FeedResponse.FromConfig(entity));
            }

            return feeds.OrderBy(f => f.Id).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve feeds");
            throw new FeedManagementException($"Failed to retrieve feeds: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get a specific feed configuration by ID.
    /// </summary>
    public async Task<FeedResponse?> GetFeedAsync(string feedId, CancellationToken ct = default)
    {
        var feedConfig = await GetFeedByIdAsync(feedId, ct);
        return feedConfig != null ? FeedResponse.FromConfig(feedConfig) : null;
    }

    /// <summary>
    /// Delete a feed configuration.
    /// </summary>
    public async Task<bool> DeleteFeedAsync(string feedId, CancellationToken ct = default)
    {
        try
        {
            var response = await _feedConfigsTable.DeleteEntityAsync("feed", feedId, cancellationToken: ct);

            _logger.LogInformation("Deleted feed {FeedId}", feedId);
            return true;
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            _logger.LogWarning("Attempted to delete non-existent feed {FeedId}", feedId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete feed {FeedId}", feedId);
            throw new FeedManagementException($"Failed to delete feed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Update the processing state for a feed (called by the polling function).
    /// </summary>
    public async Task UpdateFeedStateAsync(
        string feedId,
        string? lastEntryId = null,
        DateTimeOffset? lastEntryPublished = null,
        long? additionalEntriesProcessed = null,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        try
        {
            var feedConfig = await GetFeedByIdAsync(feedId, ct);
            if (feedConfig == null)
            {
                _logger.LogWarning("Attempted to update state for non-existent feed {FeedId}", feedId);
                return;
            }

            // Update state fields
            feedConfig.LastChecked = DateTimeOffset.UtcNow;
            feedConfig.UpdatedAt = DateTimeOffset.UtcNow;

            if (lastEntryId != null)
                feedConfig.LastEntryId = lastEntryId;

            if (lastEntryPublished.HasValue)
                feedConfig.LastEntryPublished = lastEntryPublished.Value;

            if (additionalEntriesProcessed.HasValue)
                feedConfig.TotalEntriesProcessed += additionalEntriesProcessed.Value;

            if (errorMessage != null)
            {
                feedConfig.LastError = errorMessage;
                feedConfig.LastErrorAt = DateTimeOffset.UtcNow;
            }
            else
            {
                // Clear error on successful processing
                feedConfig.LastError = null;
                feedConfig.LastErrorAt = null;
            }

            await _feedConfigsTable.UpsertEntityAsync(feedConfig, TableUpdateMode.Replace, ct);

            _logger.LogDebug(
                "Updated feed state {FeedId}: lastEntry={LastEntry}, processed={Processed}, error={Error}",
                feedId, lastEntryId, additionalEntriesProcessed, errorMessage != null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update feed state {FeedId}", feedId);
            throw new FeedManagementException($"Failed to update feed state: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get all enabled feeds for polling.
    /// </summary>
    public async Task<List<FeedConfig>> GetEnabledFeedsAsync(CancellationToken ct = default)
    {
        try
        {
            var feeds = new List<FeedConfig>();

            await foreach (var entity in _feedConfigsTable.QueryAsync<FeedConfig>(
                filter: $"PartitionKey eq 'feed' and Enabled eq true",
                cancellationToken: ct))
            {
                feeds.Add(entity);
            }

            return feeds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve enabled feeds");
            throw new FeedManagementException($"Failed to retrieve enabled feeds: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Toggle a feed's enabled status.
    /// </summary>
    public async Task<FeedResponse?> SetFeedEnabledAsync(
        string feedId,
        bool enabled,
        CancellationToken ct = default)
    {
        try
        {
            var feedConfig = await GetFeedByIdAsync(feedId, ct);
            if (feedConfig == null)
                return null;

            feedConfig.Enabled = enabled;
            feedConfig.UpdatedAt = DateTimeOffset.UtcNow;

            await _feedConfigsTable.UpsertEntityAsync(feedConfig, TableUpdateMode.Replace, ct);

            _logger.LogInformation("Set feed {FeedId} enabled = {Enabled}", feedId, enabled);
            return FeedResponse.FromConfig(feedConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set feed enabled status {FeedId}", feedId);
            throw new FeedManagementException($"Failed to update feed: {ex.Message}", ex);
        }
    }

    private async Task<FeedConfig?> GetFeedByIdAsync(string feedId, CancellationToken ct = default)
    {
        try
        {
            var response = await _feedConfigsTable.GetEntityAsync<FeedConfig>("feed", feedId, cancellationToken: ct);
            return response.Value;
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return null;
        }
    }

    private async Task<ValidationResult> ValidateCreateRequest(CreateFeedRequest request, CancellationToken ct)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.FeedUrl))
            return ValidationResult.Invalid("Feed URL is required");

        if (string.IsNullOrWhiteSpace(request.AgentName))
            return ValidationResult.Invalid("Agent name is required");

        if (string.IsNullOrWhiteSpace(request.SourceType))
            return ValidationResult.Invalid("Source type is required");

        // Validate agent name
        if (!_validAgents.Contains(request.AgentName))
        {
            var validAgentsList = string.Join(", ", _validAgents.Order());
            return ValidationResult.Invalid($"Invalid agent name. Valid agents: {validAgentsList}");
        }

        // Validate source type format
        if (!IsValidSourceType(request.SourceType))
            return ValidationResult.Invalid("Source type must contain only letters, numbers, and hyphens");

        // Validate custom ID if provided
        if (!string.IsNullOrWhiteSpace(request.Id) && !IsValidFeedId(request.Id))
            return ValidationResult.Invalid("ID must contain only letters, numbers, and hyphens");

        // Validate URL format and accessibility
        try
        {
            var feedValidation = await _atomFeedService.ValidateFeed(request.FeedUrl, ct);
            if (!feedValidation.IsValid)
                return ValidationResult.Invalid($"Invalid feed: {feedValidation.ErrorMessage}");

            return ValidationResult.Valid();
        }
        catch (Exception ex)
        {
            return ValidationResult.Invalid($"Feed validation failed: {ex.Message}");
        }
    }

    private static string GenerateFeedId(string feedUrl, string sourceType)
    {
        try
        {
            var uri = new Uri(feedUrl);
            var host = uri.Host.Replace("www.", "");
            var pathPart = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

            var basePart = string.IsNullOrEmpty(pathPart) ? host : $"{host}-{pathPart}";
            var id = $"{basePart}-{sourceType}";

            return SanitizeFeedId(id);
        }
        catch
        {
            // Fallback to simple generation
            var hash = Math.Abs(feedUrl.GetHashCode()).ToString();
            return $"feed-{sourceType}-{hash}";
        }
    }

    private static string SanitizeFeedId(string id)
    {
        // Replace invalid characters with hyphens and remove duplicates
        var sanitized = Regex.Replace(id.ToLowerInvariant(), @"[^a-z0-9\-]", "-");
        sanitized = Regex.Replace(sanitized, @"-+", "-");
        return sanitized.Trim('-');
    }

    private static bool IsValidFeedId(string id)
    {
        return !string.IsNullOrWhiteSpace(id) &&
               Regex.IsMatch(id, @"^[a-zA-Z0-9\-]+$") &&
               id.Length <= 100;
    }

    private static bool IsValidSourceType(string sourceType)
    {
        return !string.IsNullOrWhiteSpace(sourceType) &&
               Regex.IsMatch(sourceType, @"^[a-zA-Z0-9\-]+$") &&
               sourceType.Length <= 50;
    }

    private static bool IsNotFoundError(Exception ex)
    {
        return ex.Message.Contains("404") || ex.Message.Contains("Not Found");
    }

    private sealed class ValidationResult
    {
        public bool IsValid { get; }
        public string? ErrorMessage { get; }

        private ValidationResult(bool isValid, string? errorMessage)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }

        public static ValidationResult Valid() => new(true, null);
        public static ValidationResult Invalid(string errorMessage) => new(false, errorMessage);
    }
}

/// <summary>
/// Exception thrown when feed validation fails.
/// </summary>
public sealed class FeedValidationException : Exception
{
    public FeedValidationException(string message) : base(message) { }
    public FeedValidationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when feed management operations fail.
/// </summary>
public sealed class FeedManagementException : Exception
{
    public FeedManagementException(string message) : base(message) { }
    public FeedManagementException(string message, Exception innerException) : base(message, innerException) { }
}
