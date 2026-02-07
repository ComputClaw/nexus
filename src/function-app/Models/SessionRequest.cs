namespace Nexus.Ingest.Models;

public sealed class SessionRequest
{
    public string AgentId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
}
