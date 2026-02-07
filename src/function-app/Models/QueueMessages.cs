namespace Nexus.Ingest.Models;

public sealed class EmailQueueMessage
{
    public string ResourcePath { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
}

public sealed class CalendarQueueMessage
{
    public string ResourcePath { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
}

public sealed class MeetingQueueMessage
{
    public string MeetingId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
}
