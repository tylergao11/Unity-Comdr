namespace UnityComdr.Util;

/// <summary>Cheap nearest-name helper for actionable not-found errors (A4).</summary>
public static class NameSuggest
{
    /// <summary>
    /// Pick the closest candidate by StartsWith preference, then Levenshtein distance.
    /// Returns null when nothing is reasonably close.
    /// </summary>
    public static string? Nearest(string target, IEnumerable<string> candidates, int maxDistance = 4)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var list = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list.Count == 0) return null;

        var starts = list
            .Where(c => c.StartsWith(target, StringComparison.OrdinalIgnoreCase) ||
                        target.StartsWith(c, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Length)
            .FirstOrDefault();
        if (starts != null) return starts;

        string? best = null;
        var bestDist = int.MaxValue;
        foreach (var c in list)
        {
            var d = Levenshtein(target, c);
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }

        return bestDist <= maxDistance ? best : null;
    }

    public static int Levenshtein(string a, string b)
    {
        a ??= "";
        b ??= "";
        var n = a.Length;
        var m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;
        var prev = new int[m + 1];
        var cur = new int[m + 1];
        for (var j = 0; j <= m; j++) prev[j] = j;
        for (var i = 1; i <= n; i++)
        {
            cur[0] = i;
            var ca = char.ToLowerInvariant(a[i - 1]);
            for (var j = 1; j <= m; j++)
            {
                var cost = ca == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[m];
    }
}
