using System.Text;

namespace UnityComdr.ClientConfig;

/// <summary>
/// Pure (no UnityEditor) helpers for MCP client config: host path resolution,
/// project-local mcp.json / Codex TOML, and Cursor/VS Code install deeplinks (FR-I1/I2).
/// Pattern port from Coplay Clients/ + Cursor/VS Code documented mcp/install deeplink shapes.
/// Intentionally avoids System.Text.Json so the same file can compile in the UPM Editor package.
/// </summary>
public static class ClientConfigBuilder
{
    public const string DefaultServerName = "unity-comdr";
    public const string RelativeReleaseHostDll =
        "src/UnityComdr.McpHost/bin/Release/net8.0/UnityComdr.McpHost.dll";
    public const string RelativeDebugHostDll =
        "src/UnityComdr.McpHost/bin/Debug/net8.0/UnityComdr.McpHost.dll";

    /// <summary>Discover Release then Debug host DLL under a Unity/repo project root.</summary>
    public static string ResolveHostDllPath(string projectRoot, bool preferRelease = true)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("projectRoot is required", nameof(projectRoot));

        var root = Path.GetFullPath(projectRoot.Trim());
        var release = Path.GetFullPath(Path.Combine(root, RelativeReleaseHostDll.Replace('/', Path.DirectorySeparatorChar)));
        var debug = Path.GetFullPath(Path.Combine(root, RelativeDebugHostDll.Replace('/', Path.DirectorySeparatorChar)));

        if (preferRelease)
        {
            if (File.Exists(release)) return release;
            if (File.Exists(debug)) return debug;
            return release; // expected path even if missing (doctor reports existence)
        }

        if (File.Exists(debug)) return debug;
        if (File.Exists(release)) return release;
        return debug;
    }

    /// <summary>
    /// Prefer a project-relative forward-slash path when <paramref name="absoluteHostPath"/>
    /// is under <paramref name="projectRoot"/>; otherwise return an absolute forward-slash path.
    /// </summary>
    public static string ToConfigHostPath(string projectRoot, string absoluteHostPath)
    {
        if (string.IsNullOrWhiteSpace(absoluteHostPath))
            throw new ArgumentException("absoluteHostPath is required", nameof(absoluteHostPath));

        var abs = Path.GetFullPath(absoluteHostPath.Trim());
        if (string.IsNullOrWhiteSpace(projectRoot))
            return NormalizeSlashes(abs);

        var root = Path.GetFullPath(projectRoot.Trim());
        if (!abs.StartsWith(AppendDirSep(root), StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(abs, root, StringComparison.OrdinalIgnoreCase))
            return NormalizeSlashes(abs);

        var rel = abs.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return NormalizeSlashes(rel);
    }

    public static string GetProjectLocalConfigRelativePath(McpClientKind kind)
    {
        switch (kind)
        {
            case McpClientKind.Cursor: return ".cursor/mcp.json";
            case McpClientKind.VsCode: return ".vscode/mcp.json";
            case McpClientKind.ClaudeCode: return ".claude/mcp.json";
            case McpClientKind.CodexCli: return ".codex/config.toml";
            default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>Stdio server object for Cursor deeplink <c>config</c> (no name wrapper).</summary>
    public static string BuildStdioServerConfigJson(string hostDllPathForConfig)
    {
        var path = NormalizeSlashes(hostDllPathForConfig);
        return "{\"command\":\"dotnet\",\"args\":[\"exec\"," + QuoteJson(path) + "]}";
    }

    /// <summary>Cursor / Claude-style root: <c>{ "mcpServers": { "unity-comdr": {…} } }</c>.</summary>
    public static string BuildMcpServersJson(string hostDllPathForConfig, string serverName = DefaultServerName)
    {
        var inner = BuildStdioServerConfigJson(hostDllPathForConfig);
        return "{\n  \"mcpServers\": {\n    " + QuoteJson(serverName) + ": " + inner + "\n  }\n}\n";
    }

    /// <summary>VS Code Copilot layout: <c>{ "servers": { "unity-comdr": { "type":"stdio", … } } }</c>.</summary>
    public static string BuildVsCodeMcpJson(string hostDllPathForConfig, string serverName = DefaultServerName)
    {
        var path = NormalizeSlashes(hostDllPathForConfig);
        return
            "{\n" +
            "  \"servers\": {\n" +
            "    " + QuoteJson(serverName) + ": {\n" +
            "      \"type\": \"stdio\",\n" +
            "      \"command\": \"dotnet\",\n" +
            "      \"args\": [\"exec\", " + QuoteJson(path) + "]\n" +
            "    }\n" +
            "  }\n" +
            "}\n";
    }

    public static string BuildCodexToml(string hostDllPathForConfig, string serverName = DefaultServerName)
    {
        var path = NormalizeSlashes(hostDllPathForConfig);
        return
            "[mcp_servers." + serverName + "]\n" +
            "command = \"dotnet\"\n" +
            "args = [\"exec\", \"" + path.Replace("\"", "\\\"") + "\"]\n";
    }

    public static string BuildProjectLocalConfigContent(McpClientKind kind, string hostDllPathForConfig)
    {
        switch (kind)
        {
            case McpClientKind.Cursor:
            case McpClientKind.ClaudeCode:
                return BuildMcpServersJson(hostDllPathForConfig);
            case McpClientKind.VsCode:
                return BuildVsCodeMcpJson(hostDllPathForConfig);
            case McpClientKind.CodexCli:
                return BuildCodexToml(hostDllPathForConfig);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>
    /// Cursor install deeplink:
    /// <c>cursor://anysphere.cursor-deeplink/mcp/install?name=…&amp;config=BASE64JSON</c>
    /// where config is the stdio object (no name), standard base64 of UTF-8 JSON.
    /// </summary>
    public static string BuildCursorDeeplink(string hostDllPathForConfig, string serverName = DefaultServerName)
    {
        var configJson = BuildStdioServerConfigJson(hostDllPathForConfig);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson));
        return "cursor://anysphere.cursor-deeplink/mcp/install?name=" +
               Uri.EscapeDataString(serverName) +
               "&config=" + Uri.EscapeDataString(b64);
    }

    /// <summary>
    /// VS Code install deeplink (documented):
    /// <c>vscode://mcp/install?</c> + URL-encoded JSON including <c>name</c> + stdio fields.
    /// </summary>
    public static string BuildVsCodeDeeplink(string hostDllPathForConfig, string serverName = DefaultServerName)
    {
        var path = NormalizeSlashes(hostDllPathForConfig);
        var json =
            "{\"name\":" + QuoteJson(serverName) +
            ",\"type\":\"stdio\",\"command\":\"dotnet\",\"args\":[\"exec\"," + QuoteJson(path) + "]}";
        return "vscode://mcp/install?" + Uri.EscapeDataString(json);
    }

    public static bool SupportsDeeplink(McpClientKind kind) =>
        kind == McpClientKind.Cursor || kind == McpClientKind.VsCode;

    public static string? BuildDeeplink(McpClientKind kind, string hostDllPathForConfig, string serverName = DefaultServerName)
    {
        switch (kind)
        {
            case McpClientKind.Cursor: return BuildCursorDeeplink(hostDllPathForConfig, serverName);
            case McpClientKind.VsCode: return BuildVsCodeDeeplink(hostDllPathForConfig, serverName);
            default: return null;
        }
    }

    /// <summary>Decode Cursor deeplink <c>config</c> query (for tests / doctor).</summary>
    public static string DecodeCursorDeeplinkConfig(string deeplinkUrl)
    {
        if (string.IsNullOrWhiteSpace(deeplinkUrl))
            throw new ArgumentException("deeplinkUrl is required", nameof(deeplinkUrl));

        var uri = new Uri(deeplinkUrl);
        var query = uri.Query.TrimStart('?');
        string? config = null;
        foreach (var part in query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = part.Substring(0, eq);
            if (!string.Equals(key, "config", StringComparison.OrdinalIgnoreCase)) continue;
            config = Uri.UnescapeDataString(part.Substring(eq + 1));
            break;
        }

        if (string.IsNullOrEmpty(config))
            throw new InvalidOperationException("deeplink missing config query parameter");

        var bytes = Convert.FromBase64String(config);
        return Encoding.UTF8.GetString(bytes);
    }

    public static string NormalizeSlashes(string path) =>
        (path ?? "").Replace('\\', '/');

    public static string QuoteJson(string s)
    {
        if (s == null) return "null";
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

    private static string AppendDirSep(string root)
    {
        if (root.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
            root.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            return root;
        return root + Path.DirectorySeparatorChar;
    }
}
