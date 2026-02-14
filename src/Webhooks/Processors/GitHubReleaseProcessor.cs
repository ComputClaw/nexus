using System.Text.Json;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Webhooks.Processors;

public sealed class GitHubReleaseProcessor : IWebhookProcessor
{
    public string Source => "github";
    public string Type => "release";

    public Task<IngestionItem?> ProcessAsync(WebhookMessage webhook, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(
            webhook.NotificationData.GetRawText());

        var releasePayload = new
        {
            action = payload.TryGetProperty("action", out var action) ? action.GetString() : null,
            release = payload.TryGetProperty("release", out var release) ? new
            {
                id = release.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                tagName = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null,
                name = release.TryGetProperty("name", out var name) ? name.GetString() : null,
                body = release.TryGetProperty("body", out var body) ? body.GetString() : null,
                htmlUrl = release.TryGetProperty("html_url", out var url) ? url.GetString() : null,
                prerelease = release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean(),
                publishedAt = release.TryGetProperty("published_at", out var pub) ? pub.GetString() : null,
                author = release.TryGetProperty("author", out var auth) && auth.TryGetProperty("login", out var login)
                    ? login.GetString() : null
            } : null,
            repository = payload.TryGetProperty("repository", out var repo) ? new
            {
                fullName = repo.TryGetProperty("full_name", out var fn) ? fn.GetString() : null,
                htmlUrl = repo.TryGetProperty("html_url", out var rUrl) ? rUrl.GetString() : null
            } : null
        };

        var publishedAt = !string.IsNullOrEmpty(releasePayload.release?.publishedAt) &&
                          DateTimeOffset.TryParse(releasePayload.release.publishedAt, out var parsed)
            ? parsed
            : new DateTimeOffset(webhook.ReceivedAt, TimeSpan.Zero);

        var item = new IngestionItem
        {
            SourceType = "github-release",
            AgentName = webhook.AgentName,
            Payload = releasePayload,
            ReceivedAt = publishedAt
        };

        return Task.FromResult<IngestionItem?>(item);
    }
}
