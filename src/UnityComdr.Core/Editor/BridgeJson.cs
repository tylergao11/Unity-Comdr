using System.Globalization;
using System.Text;

namespace UnityComdr.Editor;

/// <summary>
/// Shared JSON string helpers for the live TCP bridge protocol.
/// Unity package mirrors the same unescape rules; unit-tested here so live script_write
/// multi-line content cannot regress without a failing test.
/// </summary>
public static class BridgeJson
{
    /// <summary>
    /// Extract a JSON string property value with standard JSON unescaping
    /// (\n \r \t \\ \" \/ \uXXXX). Returns null if key missing or value is null.
    /// </summary>
    public static string? ExtractString(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
        var marker = "\"" + key + "\":";
        var idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + marker.Length;
        while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
        if (start >= json.Length) return null;
        if (json[start] == 'n') return null; // null
        if (json[start] != '"') return null;
        start++;
        var sb = new StringBuilder();
        for (var i = start; i < json.Length; i++)
        {
            var c = json[i];
            if (c == '\\' && i + 1 < json.Length)
            {
                var n = json[i + 1];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'u':
                        if (i + 5 < json.Length &&
                            int.TryParse(json.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            sb.Append((char)code);
                            i += 5;
                            continue;
                        }
                        sb.Append(n);
                        break;
                    default:
                        sb.Append(n);
                        break;
                }
                i++;
                continue;
            }
            if (c == '"') break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extract a JSON string array property, e.g. "gameObjectIds":["a","b"].
    /// Also accepts a single string value (returned as one-element list).
    /// </summary>
    public static IReadOnlyList<string> ExtractStringArray(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            return Array.Empty<string>();
        var marker = "\"" + key + "\":";
        var idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return Array.Empty<string>();
        var start = idx + marker.Length;
        while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
        if (start >= json.Length) return Array.Empty<string>();
        if (json[start] == 'n') return Array.Empty<string>(); // null
        if (json[start] == '"')
        {
            var single = ExtractString(json, key);
            return string.IsNullOrEmpty(single) ? Array.Empty<string>() : new[] { single };
        }
        if (json[start] != '[') return Array.Empty<string>();
        start++;
        var list = new List<string>();
        while (start < json.Length)
        {
            while (start < json.Length && (char.IsWhiteSpace(json[start]) || json[start] == ',')) start++;
            if (start >= json.Length || json[start] == ']') break;
            if (json[start] != '"') break;
            start++;
            var sb = new StringBuilder();
            for (; start < json.Length; start++)
            {
                var c = json[start];
                if (c == '\\' && start + 1 < json.Length)
                {
                    var n = json[start + 1];
                    sb.Append(n switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '"' => '"',
                        '\\' => '\\',
                        _ => n
                    });
                    start++;
                    continue;
                }
                if (c == '"') { start++; break; }
                sb.Append(c);
            }
            list.Add(sb.ToString());
        }
        return list;
    }

    /// <summary>
    /// Encode a .NET string as a JSON string literal (for bridge payloads).
    /// </summary>
    public static string Quote(string? s)
    {
        if (s is null) return "null";
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
