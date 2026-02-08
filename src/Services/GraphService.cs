using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Nexus.Ingest.Services;

/// <summary>
/// Thin wrapper around Microsoft Graph SDK for fetching mail and calendar resources.
/// </summary>
public sealed class GraphService
{
    private readonly GraphServiceClient _client;
    private readonly string _userId;
    private readonly ILogger<GraphService> _logger;

    public GraphService(
        GraphServiceClient client,
        IConfiguration config,
        ILogger<GraphService> logger)
    {
        _client = client;
        _userId = config["Graph:UserId"]
            ?? throw new InvalidOperationException("Graph:UserId not configured");
        _logger = logger;
    }

    public async Task<Message?> FetchMessage(string resourcePath, CancellationToken ct)
    {
        var messageId = ExtractId(resourcePath);
        try
        {
            return await _client.Users[_userId].Messages[messageId]
                .GetAsync(config =>
                {
                    config.QueryParameters.Select = new[]
                    {
                        "id", "subject", "bodyPreview", "body", "uniqueBody",
                        "from", "toRecipients", "ccRecipients", "receivedDateTime",
                        "sentDateTime", "importance", "isRead", "hasAttachments",
                        "internetMessageId", "conversationId", "parentFolderId"
                    };
                }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch message {MessageId} from Graph", messageId);
            return null;
        }
    }

    public async Task<Event?> FetchEvent(string resourcePath, CancellationToken ct)
    {
        var eventId = ExtractId(resourcePath);
        try
        {
            return await _client.Users[_userId].Events[eventId]
                .GetAsync(config =>
                {
                    config.QueryParameters.Select = new[]
                    {
                        "id", "subject", "body", "start", "end", "location",
                        "organizer", "attendees", "isAllDay", "isCancelled",
                        "isOnlineMeeting", "onlineMeetingUrl", "recurrence"
                    };
                }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch event {EventId} from Graph", eventId);
            return null;
        }
    }

    /// <summary>
    /// Extract the resource ID from a Graph notification resource path.
    /// e.g., "users/{guid}/messages/{id}" → "{id}"
    /// </summary>
    private static string ExtractId(string resourcePath)
    {
        var lastSlash = resourcePath.LastIndexOf('/');
        return lastSlash >= 0 ? resourcePath[(lastSlash + 1)..] : resourcePath;
    }
}
