using System.Text.RegularExpressions;

namespace Nexus.Ingest.Helpers;

public static partial class HtmlStripper
{
    /// <summary>
    /// Strip HTML tags and decode common entities to get plain text.
    /// </summary>
    public static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        // Remove style and script blocks
        var result = StyleScriptRegex().Replace(html, "");
        // Remove tags
        result = TagRegex().Replace(result, "");
        // Decode common entities
        result = result
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'");
        // Collapse whitespace
        result = WhitespaceRegex().Replace(result, " ");
        return result.Trim();
    }

    [GeneratedRegex(@"<(style|script)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleScriptRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
