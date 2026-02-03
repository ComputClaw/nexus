using System.Text.Json.Serialization;

namespace Nexus.Ingest.Models;

public sealed class FirefliesPayload
{
    [JsonPropertyName("meetingId")]
    public string MeetingId { get; set; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("clientReferenceId")]
    public string? ClientReferenceId { get; set; }
}

public sealed class FirefliesGraphQLResponse
{
    [JsonPropertyName("data")]
    public FirefliesData? Data { get; set; }
}

public sealed class FirefliesData
{
    [JsonPropertyName("transcript")]
    public FirefliesTranscript? Transcript { get; set; }
}

public sealed class FirefliesTranscript
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("organizer_email")]
    public string? OrganizerEmail { get; set; }

    [JsonPropertyName("participants")]
    public List<string>? Participants { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("dateString")]
    public string DateString { get; set; } = string.Empty;

    [JsonPropertyName("transcript_url")]
    public string? TranscriptUrl { get; set; }

    [JsonPropertyName("meeting_link")]
    public string? MeetingLink { get; set; }

    [JsonPropertyName("calendar_id")]
    public string? CalendarId { get; set; }

    [JsonPropertyName("speakers")]
    public List<FirefliesSpeaker>? Speakers { get; set; }

    [JsonPropertyName("sentences")]
    public List<FirefliesSentence>? Sentences { get; set; }

    [JsonPropertyName("summary")]
    public FirefliesSummary? Summary { get; set; }

    [JsonPropertyName("meeting_attendees")]
    public List<FirefliesAttendee>? MeetingAttendees { get; set; }
}

public sealed class FirefliesSpeaker
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class FirefliesSentence
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("start_time")]
    public double StartTime { get; set; }

    [JsonPropertyName("end_time")]
    public double EndTime { get; set; }

    [JsonPropertyName("speaker_id")]
    public int SpeakerId { get; set; }

    [JsonPropertyName("speaker_name")]
    public string? SpeakerName { get; set; }
}

public sealed class FirefliesSummary
{
    [JsonPropertyName("gist")]
    public string? Gist { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("action_items")]
    public string? ActionItems { get; set; }

    [JsonPropertyName("keywords")]
    public string? Keywords { get; set; }

    [JsonPropertyName("short_summary")]
    public string? ShortSummary { get; set; }

    [JsonPropertyName("meeting_type")]
    public string? MeetingType { get; set; }

    [JsonPropertyName("topics_discussed")]
    public string? TopicsDiscussed { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public sealed class FirefliesAttendee
{
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
