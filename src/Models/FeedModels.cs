using Azure;
using Azure.Data.Tables;
using System.Text.Json.Serialization;

namespace Nexus.Ingest.Models;

/// <summary>
/// Feed configuration stored in Azure Table Storage.
/// </summary>
public sealed class FeedConfig : ITableEntity
{
    /// <summary>Unique identifier for the feed (used as RowKey)</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>URL of the Atom/RSS feed to monitor</summary>
    public string FeedUrl { get; set; } = string.Empty;

    /// <summary>Target OpenClaw agent for new entries</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>Source type for ingested items (e.g., "github-release")</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Whether this feed is actively monitored</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>ID of the last processed entry (to detect new ones)</summary>
    public string? LastEntryId { get; set; }

    /// <summary>Timestamp when the feed was last checked</summary>
    public DateTimeOffset? LastChecked { get; set; }

    /// <summary>Published timestamp of the last processed entry</summary>
    public DateTimeOffset? LastEntryPublished { get; set; }

    /// <summary>When this feed configuration was created</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this feed configuration was last modified</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Number of entries processed from this feed (for stats)</summary>
    public long TotalEntriesProcessed { get; set; } = 0;

    /// <summary>Last error message, if any</summary>
    public string? LastError { get; set; }

    /// <summary>When the last error occurred</summary>
    public DateTimeOffset? LastErrorAt { get; set; }

    // ITableEntity implementation
    public string PartitionKey { get; set; } = "feed";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public FeedConfig()
    {
        // Parameterless constructor for deserialization
    }

    public FeedConfig(string id, string feedUrl, string agentName, string sourceType)
    {
        Id = id;
        RowKey = id;
        FeedUrl = feedUrl;
        AgentName = agentName;
        SourceType = sourceType;
    }
}

/// <summary>
/// Parsed entry from an Atom/RSS feed.
/// </summary>
public sealed class AtomEntry
{
    /// <summary>Unique identifier for this entry (usually from feed)</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Entry title</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Entry link/URL</summary>
    public string Link { get; set; } = string.Empty;

    /// <summary>When this entry was published</summary>
    public DateTimeOffset Published { get; set; }

    /// <summary>When this entry was last updated</summary>
    public DateTimeOffset? Updated { get; set; }

    /// <summary>Entry content/description</summary>
    public string? Content { get; set; }

    /// <summary>Entry summary (often shorter than content)</summary>
    public string? Summary { get; set; }

    /// <summary>Author name</summary>
    public string? Author { get; set; }

    /// <summary>Author email</summary>
    public string? AuthorEmail { get; set; }

    /// <summary>Categories/tags for this entry</summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>Raw XML element for this entry (for debugging)</summary>
    [JsonIgnore]
    public string? RawXml { get; set; }
}

/// <summary>
/// Request model for creating a new feed.
/// </summary>
public sealed class CreateFeedRequest
{
    /// <summary>URL of the Atom/RSS feed to monitor</summary>
    public string FeedUrl { get; set; } = string.Empty;

    /// <summary>Target OpenClaw agent for new entries</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>Source type for ingested items (e.g., "github-release")</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Optional custom ID (auto-generated if not provided)</summary>
    public string? Id { get; set; }

    /// <summary>Whether the feed starts enabled (default: true)</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Response model for feed operations.
/// </summary>
public sealed class FeedResponse
{
    public string Id { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? LastEntryId { get; set; }
    public DateTimeOffset? LastChecked { get; set; }
    public DateTimeOffset? LastEntryPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long TotalEntriesProcessed { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }

    public static FeedResponse FromConfig(FeedConfig config)
    {
        return new FeedResponse
        {
            Id = config.Id,
            FeedUrl = config.FeedUrl,
            AgentName = config.AgentName,
            SourceType = config.SourceType,
            Enabled = config.Enabled,
            LastEntryId = config.LastEntryId,
            LastChecked = config.LastChecked,
            LastEntryPublished = config.LastEntryPublished,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt,
            TotalEntriesProcessed = config.TotalEntriesProcessed,
            LastError = config.LastError,
            LastErrorAt = config.LastErrorAt
        };
    }
}