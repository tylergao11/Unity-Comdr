#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;

namespace UnityComdr.ClientConfig
{
    /// <summary>
    /// Keep in sync with src/UnityComdr.Core/ClientConfig/ClientConfigBuilder.cs (tested there).
    /// Pattern port from Coplay Clients/ + Cursor/VS Code mcp/install deeplink shapes.
    /// </summary>
    public static class ClientConfigBuilder
    {
        public const string DefaultServerName = "unity-comdr";
        public const string RelativeReleaseHostDll =
            "src/UnityComdr.McpHost/bin/Release/net8.0/UnityComdr.McpHost.dll";
        public const string RelativeDebugHostDll =
            "src/UnityComdr.McpHost/bin/Debug/net8.0/UnityComdr.McpHost.dll";

        public static string ResolveHostDllPath(string projectRoot, bool preferRelease = true)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new ArgumentException("projectRoot is required", "projectRoot");

            var root = Path.GetFullPath(projectRoot.Trim());
            var release = Path.GetFullPath(Path.Combine(root, RelativeReleaseHostDll.Replace('/', Path.DirectorySeparatorChar)));
            var debug = Path.GetFullPath(Path.Combine(root, RelativeDebugHostDll.Replace('/', Path.DirectorySeparatorChar)));

            if (preferRelease)
            {
                if (File.Exists(release)) return release;
                if (File.Exists(debug)) return debug;
                return release;
            }

            if (File.Exists(debug)) return debug;
            if (File.Exists(release)) return release;
            return debug;
        }

        public static string ToConfigHostPath(string projectRoot, string absoluteHostPath)
        {
            if (string.IsNullOrWhiteSpace(absoluteHostPath))
                throw new ArgumentException("absoluteHostPath is required", "absoluteHostPath");

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
                default: throw new ArgumentOutOfRangeException("kind", kind, null);
            }
        }

        public static string BuildStdioServerConfigJson(string hostDllPathForConfig)
        {
            var path = NormalizeSlashes(hostDllPathForConfig);
            return "{\"command\":\"dotnet\",\"args\":[\"exec\"," + QuoteJson(path) + "]}";
        }

        public static string BuildMcpServersJson(string hostDllPathForConfig, string serverName = DefaultServerName)
        {
            var inner = BuildStdioServerConfigJson(hostDllPathForConfig);
            return "{\n  \"mcpServers\": {\n    " + QuoteJson(serverName) + ": " + inner + "\n  }\n}\n";
        }

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
                    throw new ArgumentOutOfRangeException("kind", kind, null);
            }
        }

        public static string BuildCursorDeeplink(string hostDllPathForConfig, string serverName = DefaultServerName)
        {
            var configJson = BuildStdioServerConfigJson(hostDllPathForConfig);
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson));
            return "cursor://anysphere.cursor-deeplink/mcp/install?name=" +
                   Uri.EscapeDataString(serverName) +
                   "&config=" + Uri.EscapeDataString(b64);
        }

        public static string BuildVsCodeDeeplink(string hostDllPathForConfig, string serverName = DefaultServerName)
        {
            var path = NormalizeSlashes(hostDllPathForConfig);
            var json =
                "{\"name\":" + QuoteJson(serverName) +
                ",\"type\":\"stdio\",\"command\":\"dotnet\",\"args\":[\"exec\"," + QuoteJson(path) + "]}";
            return "vscode://mcp/install?" + Uri.EscapeDataString(json);
        }

        public static bool SupportsDeeplink(McpClientKind kind)
        {
            return kind == McpClientKind.Cursor || kind == McpClientKind.VsCode;
        }

        public static string BuildDeeplink(McpClientKind kind, string hostDllPathForConfig, string serverName = DefaultServerName)
        {
            switch (kind)
            {
                case McpClientKind.Cursor: return BuildCursorDeeplink(hostDllPathForConfig, serverName);
                case McpClientKind.VsCode: return BuildVsCodeDeeplink(hostDllPathForConfig, serverName);
                default: return null;
            }
        }

        public static string NormalizeSlashes(string path)
        {
            return (path ?? "").Replace('\\', '/');
        }

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
}
#endif
