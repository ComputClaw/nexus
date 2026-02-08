using Azure.Storage.Queues;

namespace Nexus.Ingest.Services;

/// <summary>
/// Provides QueueClient instances for webhook processing.
/// Phase 1: Single unified queue replaces the 3-queue pattern.
/// </summary>
public sealed class QueueClientFactory
{
    /// <summary>Single unified queue for all webhook processing</summary>
    public QueueClient WebhookQueue { get; }

    // Legacy queues - keep temporarily for migration, then remove
    public QueueClient EmailQueue { get; }
    public QueueClient CalendarQueue { get; }
    public QueueClient MeetingQueue { get; }

    public QueueClientFactory(string connectionString)
    {
        var opts = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };

        // NEW: Single unified queue
        WebhookQueue = new QueueClient(connectionString, "webhook-processing", opts);

        // LEGACY: Keep for now, remove after Phase 1 complete
        EmailQueue = new QueueClient(connectionString, "email-ingest", opts);
        CalendarQueue = new QueueClient(connectionString, "calendar-ingest", opts);
        MeetingQueue = new QueueClient(connectionString, "meeting-ingest", opts);
    }
}
