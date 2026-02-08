using System.Text.Json;

namespace Nexus.Ingest.Models;

/// <summary>
/// Unified webhook message for all sources.
/// Contains agent routing info + source-specific notification data.
/// </summary>
public sealed class WebhookMessage
{
    /// <summary>Target OpenClaw agent (stewardclaw, sageclaw, etc.)</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>External source system (graph, fireflies, zoom, etc.)</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Data type (email, calendar, meeting, etc.)</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Original webhook URL for debugging</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>Raw notification payload from external system</summary>
    public JsonElement NotificationData { get; set; }

    /// <summary>UTC timestamp when webhook was received</summary>
    public DateTime ReceivedAt { get; set; }
}
