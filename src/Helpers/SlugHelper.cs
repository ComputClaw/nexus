using System.Text.RegularExpressions;

namespace Nexus.Ingest.Helpers;

public static partial class SlugHelper
{
    public static string Slugify(string input, int maxLength = 50)
    {
        if (string.IsNullOrWhiteSpace(input)) return "untitled";

        var slug = input.ToLowerInvariant()
            .Replace("æ", "ae").Replace("ø", "oe").Replace("å", "aa")
            .Trim();

        slug = NonAlphanumericRegex().Replace(slug, "");
        slug = WhitespaceHyphenRegex().Replace(slug, "-");
        slug = slug.Trim('-');

        return slug.Length > maxLength ? slug[..maxLength].TrimEnd('-') : slug;
    }

    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex(@"[\s-]+")]
    private static partial Regex WhitespaceHyphenRegex();
}
