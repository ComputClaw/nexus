using System.Text;
using Azure.Storage.Blobs;
using Microsoft.Graph.Models;
using Nexus.Ingest.Helpers;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Services;

public sealed class BlobStorageService
{
    private readonly BlobContainerClient _transcriptContainer;
    private readonly BlobContainerClient _emailContainer;

    public BlobStorageService(BlobServiceClient blobService)
    {
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
    /// Store full email body in blob storage (monthly folders for easy cleanup).
    /// </summary>
    public async Task<string> StoreEmailBody(Message message, CancellationToken ct)
    {
        var date = message.ReceivedDateTime ?? message.SentDateTime ?? DateTimeOffset.UtcNow;
        var shortId = SanitizeId(message.Id ?? "unknown", 20);
        var blobName = $"{date:yyyy-MM}/{shortId}.txt";

        var content = HtmlStripper.StripHtml(message.Body?.Content);
        var blob = _emailContainer.GetBlobClient(blobName);
        await blob.UploadAsync(BinaryData.FromString(content), overwrite: true, ct);

        return blobName;
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

    private static string SanitizeId(string id, int maxLength)
    {
        var clean = id.Replace("/", "").Replace("\\", "").Replace("#", "").Replace("?", "");
        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }
}
