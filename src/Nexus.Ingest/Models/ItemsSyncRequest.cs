namespace Nexus.Ingest.Models;

public sealed class ItemsSyncRequest
{
    public List<ItemReference> Items { get; set; } = new();
}

public sealed class ItemReference
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
}
