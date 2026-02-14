using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Feeds;

/// <summary>
/// Timer-triggered function that polls configured Atom/RSS feeds for new entries.
/// Processes all enabled feeds and stores new entries via IngestionService.
/// </summary>
public sealed class AtomFeedPollerFunction
{
    private readonly FeedManagementService _feedManagementService;
    private readonly AtomFeedService _atomFeedService;
    private readonly IngestionService _ingestionService;
    private readonly ILogger<AtomFeedPollerFunction> _logger;

    public AtomFeedPollerFunction(
        FeedManagementService feedManagementService,
        AtomFeedService atomFeedService,
        IngestionService ingestionService,
        ILogger<AtomFeedPollerFunction> logger)
    {
        _feedManagementService = feedManagementService;
        _atomFeedService = atomFeedService;
        _ingestionService = ingestionService;
        _logger = logger;
    }

    [Function("AtomFeedPoller")]
    public async Task Run(
        [TimerTrigger("0 */30 * * * *")] TimerInfo timerInfo, // Every 30 minutes
        CancellationToken ct)
    {
        _logger.LogInformation("Starting feed polling cycle");

        var totalFeeds = 0;
        var successfulFeeds = 0;
        var totalNewEntries = 0;
        var startTime = DateTimeOffset.UtcNow;

        try
        {
            // Get all enabled feeds
            var feeds = await _feedManagementService.GetEnabledFeedsAsync(ct);
            totalFeeds = feeds.Count;

            _logger.LogInformation("Processing {FeedCount} enabled feeds", totalFeeds);

            // Process each feed
            foreach (var feed in feeds)
            {
                if (ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Feed polling cancelled");
                    break;
                }

                try
                {
                    var newEntriesCount = await ProcessFeed(feed, ct);
                    totalNewEntries += newEntriesCount;
                    successfulFeeds++;

                    // Update success state
                    await _feedManagementService.UpdateFeedStateAsync(
                        feed.Id,
                        errorMessage: null, // Clear any previous error
                        ct: ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to process feed {FeedId} ({FeedUrl}): {Error}",
                        feed.Id, feed.FeedUrl, ex.Message);

                    // Update error state
                    await _feedManagementService.UpdateFeedStateAsync(
                        feed.Id,
                        errorMessage: ex.Message,
                        ct: ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during feed polling cycle: {Error}", ex.Message);
        }

        var duration = DateTimeOffset.UtcNow - startTime;

        _logger.LogInformation(
            "Feed polling cycle completed: {SuccessfulFeeds}/{TotalFeeds} feeds processed, " +
            "{TotalNewEntries} new entries, duration: {Duration}ms",
            successfulFeeds, totalFeeds, totalNewEntries, duration.TotalMilliseconds);
    }

    /// <summary>
    /// Process a single feed and return the number of new entries found.
    /// </summary>
    private async Task<int> ProcessFeed(
        Models.FeedConfig feed,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing feed {FeedId}: {FeedUrl}", feed.Id, feed.FeedUrl);

        // Fetch new entries from the feed
        var newEntries = await _atomFeedService.FetchNewEntries(feed, ct);

        if (newEntries.Count == 0)
        {
            _logger.LogDebug("No new entries for feed {FeedId}", feed.Id);
            return 0;
        }

        _logger.LogInformation(
            "Found {NewEntryCount} new entries for feed {FeedId}",
            newEntries.Count, feed.Id);

        var processedCount = 0;
        string? latestEntryId = null;
        DateTimeOffset? latestEntryPublished = null;

        // Process entries in chronological order (oldest first)
        foreach (var entry in newEntries.OrderBy(e => e.Published))
        {
            try
            {
                // Create the payload for ingestion
                var payload = new
                {
                    feedId = feed.Id,
                    feedUrl = feed.FeedUrl,
                    entry = new
                    {
                        id = entry.Id,
                        title = entry.Title,
                        link = entry.Link,
                        published = entry.Published,
                        updated = entry.Updated,
                        content = entry.Content,
                        summary = entry.Summary,
                        author = entry.Author,
                        authorEmail = entry.AuthorEmail,
                        categories = entry.Categories
                    },
                    metadata = new
                    {
                        feedType = DetermineFeedType(entry),
                        processingTimestamp = DateTimeOffset.UtcNow
                    }
                };

                // Store the entry via IngestionService
                var itemId = await _ingestionService.StoreItem(
                    payload,
                    feed.SourceType,
                    feed.AgentName,
                    entry.Published,
                    blobs: null, // Feed entries typically don't need blob storage
                    ct: ct);

                _logger.LogDebug(
                    "Stored feed entry {EntryId} as item {ItemId} for agent {AgentName}",
                    entry.Id, itemId, feed.AgentName);

                processedCount++;
                latestEntryId = entry.Id;
                latestEntryPublished = entry.Published;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to process entry {EntryId} from feed {FeedId}: {Error}",
                    entry.Id, feed.Id, ex.Message);

                // Continue processing other entries even if one fails
            }
        }

        // Update feed state with the latest processed entry
        if (processedCount > 0)
        {
            await _feedManagementService.UpdateFeedStateAsync(
                feed.Id,
                lastEntryId: latestEntryId,
                lastEntryPublished: latestEntryPublished,
                additionalEntriesProcessed: processedCount,
                ct: ct);

            _logger.LogInformation(
                "Processed {ProcessedCount} entries for feed {FeedId}, latest: {LatestEntryId}",
                processedCount, feed.Id, latestEntryId);
        }

        return processedCount;
    }

    /// <summary>
    /// Determine feed type from entry characteristics for metadata.
    /// </summary>
    private static string DetermineFeedType(Models.AtomEntry entry)
    {
        // Try to infer feed type from entry characteristics
        if (!string.IsNullOrEmpty(entry.RawXml))
        {
            var rawXml = entry.RawXml.ToLowerInvariant();
            if (rawXml.Contains("xmlns=\"http://www.w3.org/2005/atom\"") ||
                rawXml.Contains("<entry"))
                return "atom";

            if (rawXml.Contains("<item"))
                return "rss";
        }

        // Fallback based on other characteristics
        if (!string.IsNullOrEmpty(entry.Updated?.ToString()))
            return "atom"; // RSS typically doesn't have updated field

        return "unknown";
    }
}
