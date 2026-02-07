using System.Text.Json.Serialization;

namespace Nexus.Ingest.Models;

public sealed class GraphNotificationPayload
{
    [JsonPropertyName("value")]
    public List<GraphNotification> Value { get; set; } = [];
}

public sealed class GraphNotification
{
    [JsonPropertyName("subscriptionId")]
    public string SubscriptionId { get; set; } = string.Empty;

    [JsonPropertyName("subscriptionExpirationDateTime")]
    public string? SubscriptionExpirationDateTime { get; set; }

    [JsonPropertyName("clientState")]
    public string? ClientState { get; set; }

    [JsonPropertyName("changeType")]
    public string ChangeType { get; set; } = string.Empty;

    [JsonPropertyName("resource")]
    public string Resource { get; set; } = string.Empty;

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("resourceData")]
    public GraphResourceData? ResourceData { get; set; }

    [JsonPropertyName("lifecycleEvent")]
    public string? LifecycleEvent { get; set; }
}

public sealed class GraphResourceData
{
    [JsonPropertyName("@odata.type")]
    public string? ODataType { get; set; }

    [JsonPropertyName("@odata.id")]
    public string? ODataId { get; set; }

    [JsonPropertyName("@odata.etag")]
    public string? ODataEtag { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}
