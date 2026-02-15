using Azure.Storage.Queues;

namespace Nexus.Ingest.Services;

public sealed class QueueClientFactory
{
    public QueueClient WebhookQueue { get; }

    public QueueClientFactory(string connectionString)
    {
        var opts = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        WebhookQueue = new QueueClient(connectionString, "webhook-processing", opts);
    }
}
