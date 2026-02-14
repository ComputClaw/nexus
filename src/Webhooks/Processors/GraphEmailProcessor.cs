using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Graph;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Webhooks.Processors;

public sealed class GraphEmailProcessor : IWebhookProcessor
{
    public string Source => "graph";
    public string Type => "email";

    private readonly GraphService _graphService;
    private readonly ILogger<GraphEmailProcessor> _logger;

    public GraphEmailProcessor(GraphService graphService, ILogger<GraphEmailProcessor> logger)
    {
        _graphService = graphService;
        _logger = logger;
    }

    public async Task<IngestionItem?> ProcessAsync(WebhookMessage webhook, CancellationToken ct)
    {
        var notification = JsonSerializer.Deserialize<GraphNotification>(
            webhook.NotificationData.GetRawText());

        if (notification == null)
        {
            _logger.LogWarning("Failed to parse Graph email notification");
            return null;
        }

        var message = await _graphService.FetchMessage(notification.Resource, ct);
        if (message == null)
        {
            _logger.LogWarning("Message not found in Graph: {Id}", notification.ResourceData?.Id);
            return null;
        }

        var payload = new
        {
            notification,
            message = new
            {
                id = message.Id,
                subject = message.Subject,
                from = message.From?.EmailAddress?.Address,
                to = message.ToRecipients?.Select(r => r.EmailAddress?.Address),
                cc = message.CcRecipients?.Select(r => r.EmailAddress?.Address),
                conversationId = message.ConversationId,
                receivedDateTime = message.ReceivedDateTime,
                sentDateTime = message.SentDateTime,
                bodyPreview = message.BodyPreview,
                hasAttachments = message.HasAttachments
            }
        };

        var receivedAt = message.ReceivedDateTime ?? message.SentDateTime ?? DateTimeOffset.UtcNow;

        var blobs = new Dictionary<string, BlobContent>();
        var bodyContent = message.Body?.Content;
        if (!string.IsNullOrEmpty(bodyContent))
        {
            blobs["BodyBlob"] = new BlobContent
            {
                Content = bodyContent,
                ContainerName = "emails",
                Prefix = "body",
                Extension = "html"
            };
        }

        return new IngestionItem
        {
            SourceType = "graph-email",
            AgentName = webhook.AgentName,
            Payload = payload,
            ReceivedAt = receivedAt,
            Blobs = blobs.Count > 0 ? blobs : null
        };
    }
}
