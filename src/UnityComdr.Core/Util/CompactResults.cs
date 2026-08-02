using System.Text;
using System.Text.Json;
using UnityComdr.Models;

namespace UnityComdr.Util;

/// <summary>
/// Shared helpers for token-frugal tool results (pagination + truncation).
/// </summary>
public static class CompactResults
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static int ClampPageSize(int? pageSize)
    {
        var n = pageSize ?? DefaultPageSize;
        if (n < 1) n = 1;
        if (n > MaxPageSize) n = MaxPageSize;
        return n;
    }

    public static object Paginate<T>(IReadOnlyList<T> items, int? offset, int? pageSize, Func<T, object>? map = null)
    {
        var off = Math.Max(0, offset ?? 0);
        var size = ClampPageSize(pageSize);
        var slice = items.Skip(off).Take(size).ToList();
        var mapped = map == null ? slice.Cast<object>().ToList() : slice.Select(map).ToList();
        return new
        {
            total = items.Count,
            offset = off,
            pageSize = size,
            hasMore = off + size < items.Count,
            digDeeper = off + size < items.Count
                ? $"Pass offset={off + size} pageSize={size} to continue."
                : null,
            items = mapped
        };
    }

    public static object HierarchySummary(
        IReadOnlyList<GameObjectData> all,
        IReadOnlyList<string> rootIds,
        int maxDepth = 3,
        int maxNodes = 40)
    {
        var byId = all.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
        var lines = new List<object>();
        var count = 0;

        void Walk(string id, int depth, string path)
        {
            if (count >= maxNodes || depth > maxDepth) return;
            if (!byId.TryGetValue(id, out var go)) return;
            var fullPath = string.IsNullOrEmpty(path) ? go.Name : path + "/" + go.Name;
            lines.Add(new
            {
                id = go.Id,
                name = go.Name,
                path = fullPath,
                active = go.Active,
                depth,
                components = go.Components.Select(c => c.TypeName).ToList(),
                childCount = go.ChildIds.Count
            });
            count++;
            if (depth >= maxDepth)
            {
                if (go.ChildIds.Count > 0)
                    lines.Add(new { truncated = true, path = fullPath, note = "Increase maxDepth or query children by path." });
                return;
            }
            foreach (var child in go.ChildIds)
                Walk(child, depth + 1, fullPath);
        }

        foreach (var root in rootIds)
            Walk(root, 0, "");

        return new
        {
            nodeCount = count,
            maxDepth,
            maxNodes,
            truncated = count >= maxNodes || all.Count > count,
            digDeeper = "Use gameobject_manage action=get with target id/path for full details; raise maxDepth/maxNodes carefully.",
            hierarchy = lines
        };
    }

    public static string ToJson(object value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string Truncate(string text, int maxChars = 4000)
    {
        if (text.Length <= maxChars) return text;
        return text[..maxChars] + $"\n… truncated ({text.Length - maxChars} more chars). Use smaller range or offset.";
    }

    public static string FormatLogs(IEnumerable<ConsoleLogEntry> logs)
    {
        var sb = new StringBuilder();
        foreach (var log in logs)
        {
            sb.Append('[').Append(log.Type).Append("] ").Append(log.Message);
            if (!string.IsNullOrEmpty(log.File))
                sb.Append(" @ ").Append(log.File).Append(':').Append(log.Line);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
