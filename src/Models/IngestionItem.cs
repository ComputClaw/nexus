namespace Nexus.Ingest.Models;

public class IngestionItem
{
    public required string SourceType { get; init; }
    public required string AgentName { get; init; }
    public required object Payload { get; init; }
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, BlobContent>? Blobs { get; init; }

    /// <summary>
    /// Optional sender email — when set, the processor checks the whitelist
    /// and routes non-whitelisted items to PendingEmails instead of Items.
    /// </summary>
    public string? SenderEmail { get; init; }
}

public class BlobContent
{
    public required string Content { get; init; }
    public required string ContainerName { get; init; }
    public required string Prefix { get; init; }
    public string Extension { get; init; } = "txt";
}
