using KeyNestForWin.Models;

namespace KeyNestForWin.Services;

public static class PasswordItemSearch
{
    public static string SearchHaystackLowercased(PasswordItemDto item)
    {
        var parts = new List<string>
        {
            item.Title,
            item.Username,
            item.Url,
            item.Notes,
            HostHint(item.Url)
        };
        foreach (var f in item.CustomFields)
        {
            parts.Add(f.Label);
            parts.Add(f.Value);
        }
        return string.Join('\n', parts).ToLowerInvariant();
    }

    private static string HostHint(string raw)
    {
        var t = raw.Trim();
        if (string.IsNullOrEmpty(t)) return "";
        var s = t.Contains("://", StringComparison.Ordinal) ? t : "https://" + t;
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            return uri.Host;
        return t;
    }

    /// <summary>查询按空白拆成多个词，全部命中（子串）即匹配。</summary>
    public static bool MatchesSearchTokens(PasswordItemDto item, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0) return true;
        var hay = SearchHaystackLowercased(item);
        foreach (var tok in tokens)
        {
            if (!hay.Contains(tok.ToLowerInvariant(), StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    public static List<string> SplitSearchTokens(string searchText) =>
        searchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
}
