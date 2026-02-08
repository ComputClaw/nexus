using System.Net.Http.Json;
using System.Text.Json;
using Nexus.Ingest.Models;

namespace Nexus.Ingest.Services;

/// <summary>
/// Fireflies.ai GraphQL client. Uses typed HttpClient pattern (IHttpClientFactory).
/// </summary>
public sealed class FirefliesService
{
    private readonly HttpClient _httpClient;

    public FirefliesService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<FirefliesTranscript?> FetchTranscript(string meetingId, CancellationToken ct)
    {
        var query = """
            query GetTranscript($id: String!) {
                transcript(id: $id) {
                    id title organizer_email participants duration
                    dateString transcript_url meeting_link calendar_id
                    speakers { id name }
                    sentences { index text start_time end_time speaker_id speaker_name }
                    summary {
                        gist overview action_items keywords short_summary
                        meeting_type topics_discussed notes
                    }
                    meeting_attendees { displayName email }
                }
            }
            """;

        var request = new { query, variables = new { id = meetingId } };
        var response = await _httpClient.PostAsJsonAsync("", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<FirefliesGraphQLResponse>(ct);
        return result?.Data?.Transcript;
    }
}
