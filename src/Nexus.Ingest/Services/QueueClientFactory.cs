using Azure.Storage.Queues;

namespace Nexus.Ingest.Services;

/// <summary>
/// Provides named QueueClient instances without keyed DI (not supported in Azure Functions host).
/// </summary>
public sealed class QueueClientFactory
{
    public QueueClient EmailQueue { get; }
    public QueueClient CalendarQueue { get; }
    public QueueClient MeetingQueue { get; }

    public QueueClientFactory(string connectionString)
    {
        var opts = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        EmailQueue = new QueueClient(connectionString, "email-ingest", opts);
        CalendarQueue = new QueueClient(connectionString, "calendar-ingest", opts);
        MeetingQueue = new QueueClient(connectionString, "meeting-ingest", opts);
    }
}
