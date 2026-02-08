using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Graph.Models;
using Nexus.Ingest.Helpers;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Services;

public sealed class BlobStorageService
{
    private readonly BlobContainerClient _transcriptContainer;
    private readonly BlobContainerClient _emailContainer;
    private readonly BlobServiceClient _blobService;

    public BlobStorageService(BlobServiceClient blobService)
    {
        _blobService = blobService;
        _transcriptContainer = blobService.GetBlobContainerClient("transcripts");
        _emailContainer = blobService.GetBlobContainerClient("email-bodies");
    }

    /// <summary>
    /// Call once at startup to ensure blob containers exist.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _transcriptContainer.CreateIfNotExistsAsync();
        await _emailContainer.CreateIfNotExistsAsync();
    }

    /// <summary>
    /// Store text content in blob storage with date-based path.
    /// </summary>
    public async Task<string> StoreTextContent(
        string content,
        string containerName,
        string prefix,
        string extension,
        CancellationToken ct)
    {
        var container = _blobService.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var date = DateTimeOffset.UtcNow;
        var contentHash = HashId(content);
        var blobName = $"{date:yyyy-MM}/{prefix}-{contentHash}.{extension}";

        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(BinaryData.FromString(content), overwrite: true, ct);

        return $"{containerName}/{blobName}";
    }

    /// <summary>
    /// Store full email body in blob storage (monthly folders for easy cleanup).
    /// Uses SHA256 hash of message ID to avoid collisions (Graph IDs share common prefix).
    /// </summary>
    public async Task<string> StoreEmailBody(Message message, CancellationToken ct)
    {
        var date = message.ReceivedDateTime ?? message.SentDateTime ?? DateTimeOffset.UtcNow;
        var idHash = HashId(message.Id ?? "unknown");
        var blobName = $"{date:yyyy-MM}/{idHash}.txt";

        var content = HtmlStripper.StripHtml(message.Body?.Content);
        var blob = _emailContainer.GetBlobClient(blobName);
        await blob.UploadAsync(BinaryData.FromString(content), overwrite: true, ct);

        return blobName;
    }

    /// <summary>
    /// Generate a short, collision-resistant hash from an ID.
    /// </summary>
    private static string HashId(string id)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    /// <summary>
    /// Store full meeting transcript in blob storage (monthly folders for easy cleanup).
    /// </summary>
    public async Task<string> StoreTranscript(FirefliesTranscript transcript, CancellationToken ct)
    {
        var date = DateTimeOffset.Parse(transcript.DateString);
        var blobName = $"{date:yyyy-MM}/{transcript.Id}.txt";

        var content = BuildTranscriptText(transcript);
        var blob = _transcriptContainer.GetBlobClient(blobName);
        await blob.UploadAsync(BinaryData.FromString(content), overwrite: true, ct);

        return blobName;
    }

    private static string BuildTranscriptText(FirefliesTranscript transcript)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {transcript.Title}");
        sb.AppendLine($"Date: {transcript.DateString}");
        sb.AppendLine($"Duration: {transcript.Duration} min");
        sb.AppendLine();

        foreach (var sentence in transcript.Sentences ?? [])
        {
            sb.AppendLine($"[{sentence.SpeakerName}]: {sentence.Text}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Read blob content as string. Returns null if blob doesn't exist.
    /// </summary>
    public async Task<string?> ReadBlob(string type, string blobPath, CancellationToken ct)
    {
        var container = type switch
        {
            "email" => _emailContainer,
            "meeting" => _transcriptContainer,
            _ => null
        };

        if (container == null) return null;

        try
        {
            var blob = container.GetBlobClient(blobPath);
            var response = await blob.DownloadContentAsync(ct);
            return response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static string SanitizeId(string id, int maxLength)
    {
        var clean = id.Replace("/", "").Replace("\\", "").Replace("#", "").Replace("?", "");
        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }
}
