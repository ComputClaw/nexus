namespace Nexus.Ingest.Models;

public sealed class WhitelistRequest
{
    public List<string> Domains { get; set; } = [];
    public List<string> Emails { get; set; } = [];
}

public sealed class WhitelistedDomainDto
{
    public string Domain { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }
    public string AddedBy { get; set; } = string.Empty;
    public int EmailCount { get; set; }
    /// <summary>"domain" or "email" — indicates whether this is a domain-level or email-level whitelist entry.</summary>
    public string Type { get; set; } = "domain";
}
