namespace Nexus.Ingest.Models;

public class IngestionItem
{
    public required string SourceType { get; init; }
    public required string AgentName { get; init; }
    public required object Payload { get; init; }
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, BlobContent>? Blobs { get; init; }
}

public class BlobContent
{
    public required string Content { get; init; }
    public required string ContainerName { get; init; }
    public required string Prefix { get; init; }
    public string Extension { get; init; } = "txt";
}
