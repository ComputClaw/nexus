using Microsoft.Graph.Models;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Helpers;

public static class FileNameBuilder
{
    /// <summary>
    /// Email filename: {shortId}-{sender-slug}-{subject-slug}.md
    /// Agent prepends: emails/{yyyy}/{MM}/{dd}/
    /// </summary>
    public static string ForEmail(Message message)
    {
        var senderName = message.From?.EmailAddress?.Name
            ?? message.From?.EmailAddress?.Address?.Split('@')[0]
            ?? "unknown";
        var subject = message.Subject ?? "no-subject";
        var shortId = SanitizeId(message.Id ?? "", 8);

        return $"{shortId}-{SlugHelper.Slugify(senderName, 20)}-{SlugHelper.Slugify(subject, 40)}.md";
    }

    /// <summary>
    /// Calendar filename: {shortId}-{title-slug}.md
    /// Agent prepends: calendar/{yyyy}/{MM}/{dd}/
    /// </summary>
    public static string ForCalendarEvent(Event calendarEvent)
    {
        var title = calendarEvent.Subject ?? "untitled";
        var shortId = SanitizeId(calendarEvent.Id ?? "", 8);

        return $"{shortId}-{SlugHelper.Slugify(title, 50)}.md";
    }

    /// <summary>
    /// Meeting filename: {firefliesId}-{title-slug}.md
    /// Agent prepends: meetings/{yyyy}/{MM}/{dd}/
    /// </summary>
    public static string ForMeeting(FirefliesTranscript transcript)
    {
        var title = transcript.Title ?? "untitled-meeting";
        return $"{transcript.Id}-{SlugHelper.Slugify(title, 50)}.md";
    }

    private static string SanitizeId(string id, int maxLength)
    {
        var clean = id
            .Replace("/", "")
            .Replace("\\", "")
            .Replace("#", "")
            .Replace("?", "");
        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }
}
