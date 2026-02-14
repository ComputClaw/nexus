using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Feeds;

/// <summary>
/// Service for fetching and parsing Atom/RSS feeds.
/// Handles both Atom 1.0 and RSS 2.0 formats.
/// </summary>
public sealed class AtomFeedService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AtomFeedService> _logger;

    // Common date formats found in feeds
    private static readonly string[] DateFormats = new[]
    {
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss.fffffffZ",
        "yyyy-MM-ddTHH:mm:ss.fZ",
        "yyyy-MM-ddTHH:mm:sszzz",
        "yyyy-MM-ddTHH:mm:ss.fffffffzzz",
        "ddd, dd MMM yyyy HH:mm:ss zzz", // RSS format
        "ddd, dd MMM yyyy HH:mm:ss GMT",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss"
    };

    public AtomFeedService(HttpClient httpClient, ILogger<AtomFeedService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Configure HTTP client for feed fetching
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Nexus-Feed-Monitor/1.0 (+https://github.com/ComputClaw/nexus)");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Fetch and parse a feed, returning entries newer than the specified entry ID.
    /// </summary>
    public async Task<List<AtomEntry>> FetchNewEntries(
        FeedConfig feedConfig,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Fetching feed: {FeedUrl}", feedConfig.FeedUrl);

            var content = await _httpClient.GetStringAsync(feedConfig.FeedUrl, ct);
            var document = XDocument.Parse(content);

            var entries = ParseFeed(document, feedConfig.FeedUrl);
            var newEntries = FilterNewEntries(entries, feedConfig.LastEntryId, feedConfig.LastEntryPublished);

            _logger.LogInformation(
                "Feed {FeedId}: {TotalEntries} entries, {NewEntries} new",
                feedConfig.Id, entries.Count, newEntries.Count);

            return newEntries;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching feed {FeedId}: {FeedUrl}",
                feedConfig.Id, feedConfig.FeedUrl);
            throw new FeedFetchException($"Failed to fetch feed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == ct)
        {
            _logger.LogWarning("Feed fetch cancelled: {FeedId}", feedConfig.Id);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError("Feed fetch timeout: {FeedId} ({FeedUrl})", feedConfig.Id, feedConfig.FeedUrl);
            throw new FeedFetchException("Feed fetch timeout", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing feed {FeedId}: {FeedUrl}",
                feedConfig.Id, feedConfig.FeedUrl);
            throw new FeedParseException($"Failed to parse feed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validate that a feed URL is accessible and parseable.
    /// </summary>
    public async Task<FeedValidationResult> ValidateFeed(
        string feedUrl,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Validating feed URL: {FeedUrl}", feedUrl);

            if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                return FeedValidationResult.Invalid("URL must be a valid HTTP or HTTPS URL");
            }

            var content = await _httpClient.GetStringAsync(feedUrl, ct);
            var document = XDocument.Parse(content);

            var feedType = DetectFeedType(document);
            if (feedType == FeedType.Unknown)
            {
                return FeedValidationResult.Invalid("Document is not a valid Atom or RSS feed");
            }

            var entries = ParseFeed(document, feedUrl);

            return FeedValidationResult.Valid(feedType, entries.Count);
        }
        catch (HttpRequestException ex)
        {
            return FeedValidationResult.Invalid($"HTTP error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return FeedValidationResult.Invalid("Request timeout");
        }
        catch (Exception ex)
        {
            return FeedValidationResult.Invalid($"Parse error: {ex.Message}");
        }
    }

    private List<AtomEntry> ParseFeed(XDocument document, string feedUrl)
    {
        var feedType = DetectFeedType(document);

        return feedType switch
        {
            FeedType.Atom => ParseAtomFeed(document),
            FeedType.Rss => ParseRssFeed(document),
            _ => throw new FeedParseException($"Unsupported feed type for {feedUrl}")
        };
    }

    private FeedType DetectFeedType(XDocument document)
    {
        var root = document.Root;
        if (root == null) return FeedType.Unknown;

        // Check for Atom namespace
        var atomNamespace = "http://www.w3.org/2005/Atom";
        if (root.Name.Namespace == atomNamespace || root.Name.LocalName == "feed")
            return FeedType.Atom;

        // Check for RSS
        if (root.Name.LocalName.Equals("rss", StringComparison.OrdinalIgnoreCase) ||
            root.Name.LocalName.Equals("rdf", StringComparison.OrdinalIgnoreCase))
            return FeedType.Rss;

        return FeedType.Unknown;
    }

    private List<AtomEntry> ParseAtomFeed(XDocument document)
    {
        var atomNs = document.Root?.Name.Namespace ?? "http://www.w3.org/2005/Atom";
        var entries = new List<AtomEntry>();

        var entryElements = document.Descendants(atomNs + "entry");

        foreach (var entryElement in entryElements)
        {
            try
            {
                var entry = new AtomEntry
                {
                    Id = GetElementValue(entryElement, atomNs + "id"),
                    Title = GetElementValue(entryElement, atomNs + "title"),
                    Link = GetLinkHref(entryElement, atomNs),
                    Published = ParseDate(GetElementValue(entryElement, atomNs + "published")) ?? DateTimeOffset.UtcNow,
                    Updated = ParseDate(GetElementValue(entryElement, atomNs + "updated")),
                    Content = GetContentValue(entryElement, atomNs),
                    Summary = GetElementValue(entryElement, atomNs + "summary"),
                    Author = GetAuthorName(entryElement, atomNs),
                    AuthorEmail = GetAuthorEmail(entryElement, atomNs),
                    Categories = GetCategories(entryElement, atomNs).ToList(),
                    RawXml = entryElement.ToString()
                };

                entries.Add(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing entry, skipping: {EntryXml}", entryElement.ToString());
            }
        }

        return entries.OrderByDescending(e => e.Published).ToList();
    }

    private List<AtomEntry> ParseRssFeed(XDocument document)
    {
        var entries = new List<AtomEntry>();
        var items = document.Descendants("item");

        foreach (var item in items)
        {
            try
            {
                var entry = new AtomEntry
                {
                    Id = GetElementValue(item, "guid") ?? GetElementValue(item, "link") ?? Guid.NewGuid().ToString(),
                    Title = GetElementValue(item, "title"),
                    Link = GetElementValue(item, "link"),
                    Published = ParseDate(GetElementValue(item, "pubDate")) ?? DateTimeOffset.UtcNow,
                    Content = GetElementValue(item, "description"),
                    Summary = GetElementValue(item, "description"),
                    Author = GetElementValue(item, "author") ?? GetElementValue(item, "dc:creator"),
                    Categories = GetRssCategories(item).ToList(),
                    RawXml = item.ToString()
                };

                entries.Add(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing RSS item, skipping: {ItemXml}", item.ToString());
            }
        }

        return entries.OrderByDescending(e => e.Published).ToList();
    }

    private List<AtomEntry> FilterNewEntries(
        List<AtomEntry> allEntries,
        string? lastEntryId,
        DateTimeOffset? lastEntryPublished)
    {
        if (string.IsNullOrEmpty(lastEntryId))
        {
            // First run - return only the latest entry to avoid spam
            return allEntries.Take(1).ToList();
        }

        var newEntries = new List<AtomEntry>();

        foreach (var entry in allEntries)
        {
            // Stop when we reach the last processed entry
            if (entry.Id == lastEntryId)
                break;

            // Also check by publish date as backup (feeds might reorder)
            if (lastEntryPublished.HasValue && entry.Published <= lastEntryPublished.Value)
                continue;

            newEntries.Add(entry);
        }

        return newEntries.OrderBy(e => e.Published).ToList(); // Process oldest first
    }

    private static string GetElementValue(XElement parent, XName elementName)
    {
        return parent.Element(elementName)?.Value?.Trim() ?? string.Empty;
    }

    private static string GetElementValue(XElement parent, string elementName)
    {
        return parent.Element(elementName)?.Value?.Trim() ?? string.Empty;
    }

    private static string GetLinkHref(XElement entryElement, XNamespace atomNs)
    {
        var linkElement = entryElement.Element(atomNs + "link");
        if (linkElement == null) return string.Empty;

        // Try href attribute first (Atom standard)
        var href = linkElement.Attribute("href")?.Value;
        if (!string.IsNullOrEmpty(href)) return href;

        // Fallback to element value
        return linkElement.Value?.Trim() ?? string.Empty;
    }

    private static string GetContentValue(XElement entryElement, XNamespace atomNs)
    {
        var contentElement = entryElement.Element(atomNs + "content");
        return contentElement?.Value?.Trim() ?? string.Empty;
    }

    private static string GetAuthorName(XElement entryElement, XNamespace atomNs)
    {
        var authorElement = entryElement.Element(atomNs + "author");
        if (authorElement != null)
        {
            var name = authorElement.Element(atomNs + "name")?.Value?.Trim();
            if (!string.IsNullOrEmpty(name)) return name;
        }

        return GetElementValue(entryElement, atomNs + "author");
    }

    private static string GetAuthorEmail(XElement entryElement, XNamespace atomNs)
    {
        var authorElement = entryElement.Element(atomNs + "author");
        return authorElement?.Element(atomNs + "email")?.Value?.Trim() ?? string.Empty;
    }

    private static IEnumerable<string> GetCategories(XElement entryElement, XNamespace atomNs)
    {
        return entryElement.Elements(atomNs + "category")
            .Select(c => c.Attribute("term")?.Value ?? c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim());
    }

    private static IEnumerable<string> GetRssCategories(XElement item)
    {
        return item.Elements("category")
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim());
    }

    private static DateTimeOffset? ParseDate(string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
            return null;

        // Try standard DateTimeOffset parsing first
        if (DateTimeOffset.TryParse(dateString, out var result))
            return result;

        // Try each known format
        foreach (var format in DateFormats)
        {
            if (DateTimeOffset.TryParseExact(dateString, format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out result))
            {
                return result;
            }
        }

        // Last resort: try to extract ISO date with regex
        var isoMatch = Regex.Match(dateString, @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})");
        if (isoMatch.Success && DateTime.TryParse(isoMatch.Groups[1].Value, out var dateTime))
        {
            return new DateTimeOffset(dateTime, TimeSpan.Zero);
        }

        return null;
    }
}

/// <summary>
/// Feed type enumeration.
/// </summary>
public enum FeedType
{
    Unknown,
    Atom,
    Rss
}

/// <summary>
/// Result of feed validation.
/// </summary>
public sealed class FeedValidationResult
{
    public bool IsValid { get; private set; }
    public string? ErrorMessage { get; private set; }
    public FeedType FeedType { get; private set; }
    public int EntryCount { get; private set; }

    private FeedValidationResult(bool isValid, string? errorMessage, FeedType feedType, int entryCount)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
        FeedType = feedType;
        EntryCount = entryCount;
    }

    public static FeedValidationResult Valid(FeedType feedType, int entryCount)
        => new(true, null, feedType, entryCount);

    public static FeedValidationResult Invalid(string errorMessage)
        => new(false, errorMessage, FeedType.Unknown, 0);
}

/// <summary>
/// Exception thrown when feed fetching fails.
/// </summary>
public sealed class FeedFetchException : Exception
{
    public FeedFetchException(string message) : base(message) { }
    public FeedFetchException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when feed parsing fails.
/// </summary>
public sealed class FeedParseException : Exception
{
    public FeedParseException(string message) : base(message) { }
    public FeedParseException(string message, Exception innerException) : base(message, innerException) { }
}
