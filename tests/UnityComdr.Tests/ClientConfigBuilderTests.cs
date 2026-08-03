using System.Text;
using System.Text.Json;
using UnityComdr.ClientConfig;
using Xunit;

namespace UnityComdr.Tests;

public class ClientConfigBuilderTests
{
    [Fact]
    public void ToConfigHostPath_prefers_relative_when_under_project()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "unity-comdr-cfg-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var abs = Path.GetFullPath(Path.Combine(root,
                ClientConfigBuilder.RelativeReleaseHostDll.Replace('/', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllText(abs, "dll");

            var cfgPath = ClientConfigBuilder.ToConfigHostPath(root, abs);
            Assert.Equal(ClientConfigBuilder.RelativeReleaseHostDll, cfgPath);
            Assert.DoesNotContain(":", cfgPath.Substring(1)); // not a Windows drive-rooted path
            Assert.DoesNotContain('\\', cfgPath);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ToConfigHostPath_falls_back_to_absolute_outside_project()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "unity-comdr-root-" + Guid.NewGuid().ToString("N")));
        var outside = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "unity-comdr-out-" + Guid.NewGuid().ToString("N"), "host.dll"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "x");
        try
        {
            var cfgPath = ClientConfigBuilder.ToConfigHostPath(root, outside);
            Assert.StartsWith(ClientConfigBuilder.NormalizeSlashes(Path.GetPathRoot(outside) ?? ""), cfgPath);
            Assert.Contains("host.dll", cfgPath);
            Assert.DoesNotContain('\\', cfgPath);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
            try { Directory.Delete(Path.GetDirectoryName(outside)!, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveHostDllPath_prefers_release_when_present()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "unity-comdr-host-" + Guid.NewGuid().ToString("N")));
        var release = Path.Combine(root, ClientConfigBuilder.RelativeReleaseHostDll.Replace('/', Path.DirectorySeparatorChar));
        var debug = Path.Combine(root, ClientConfigBuilder.RelativeDebugHostDll.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(release)!);
        Directory.CreateDirectory(Path.GetDirectoryName(debug)!);
        File.WriteAllText(release, "r");
        File.WriteAllText(debug, "d");
        try
        {
            var resolved = ClientConfigBuilder.ResolveHostDllPath(root);
            Assert.Equal(Path.GetFullPath(release), resolved);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void BuildMcpServersJson_relative_structure_for_cursor()
    {
        var json = ClientConfigBuilder.BuildMcpServersJson(
            ClientConfigBuilder.RelativeReleaseHostDll);

        using var doc = JsonDocument.Parse(json);
        var server = doc.RootElement.GetProperty("mcpServers").GetProperty("unity-comdr");
        Assert.Equal("dotnet", server.GetProperty("command").GetString());
        var args = server.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ToArray();
        Assert.Equal(new[] { "exec", ClientConfigBuilder.RelativeReleaseHostDll }, args);
    }

    [Fact]
    public void BuildVsCodeMcpJson_uses_servers_layout()
    {
        var json = ClientConfigBuilder.BuildVsCodeMcpJson("src/UnityComdr.McpHost/bin/Release/net8.0/UnityComdr.McpHost.dll");
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("mcpServers", out _));
        var server = doc.RootElement.GetProperty("servers").GetProperty("unity-comdr");
        Assert.Equal("stdio", server.GetProperty("type").GetString());
        Assert.Equal("dotnet", server.GetProperty("command").GetString());
    }

    [Fact]
    public void Cursor_deeplink_payload_shape_decodes_to_stdio_config()
    {
        var host = ClientConfigBuilder.RelativeReleaseHostDll;
        var url = ClientConfigBuilder.BuildCursorDeeplink(host);

        Assert.StartsWith("cursor://anysphere.cursor-deeplink/mcp/install?", url);
        Assert.Contains("name=unity-comdr", url);
        Assert.Contains("config=", url);

        var decoded = ClientConfigBuilder.DecodeCursorDeeplinkConfig(url);
        using var doc = JsonDocument.Parse(decoded);
        Assert.Equal("dotnet", doc.RootElement.GetProperty("command").GetString());
        Assert.False(doc.RootElement.TryGetProperty("name", out _), "Cursor config payload must not wrap name");
        var args = doc.RootElement.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ToArray();
        Assert.Equal(new[] { "exec", host }, args);

        // Round-trip: base64 of exact BuildStdioServerConfigJson
        Assert.Equal(ClientConfigBuilder.BuildStdioServerConfigJson(host), decoded);
    }

    [Fact]
    public void VsCode_deeplink_is_url_encoded_json_with_name()
    {
        var host = "src/UnityComdr.McpHost/bin/Release/net8.0/UnityComdr.McpHost.dll";
        var url = ClientConfigBuilder.BuildVsCodeDeeplink(host);
        Assert.StartsWith("vscode://mcp/install?", url);
        var encoded = url.Substring("vscode://mcp/install?".Length);
        var json = Uri.UnescapeDataString(encoded);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("unity-comdr", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("stdio", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("dotnet", doc.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public void Project_local_paths_and_codex_toml()
    {
        Assert.Equal(".cursor/mcp.json", ClientConfigBuilder.GetProjectLocalConfigRelativePath(McpClientKind.Cursor));
        Assert.Equal(".vscode/mcp.json", ClientConfigBuilder.GetProjectLocalConfigRelativePath(McpClientKind.VsCode));
        Assert.Equal(".claude/mcp.json", ClientConfigBuilder.GetProjectLocalConfigRelativePath(McpClientKind.ClaudeCode));
        Assert.Equal(".codex/config.toml", ClientConfigBuilder.GetProjectLocalConfigRelativePath(McpClientKind.CodexCli));

        var toml = ClientConfigBuilder.BuildCodexToml(ClientConfigBuilder.RelativeReleaseHostDll);
        Assert.Contains("[mcp_servers.unity-comdr]", toml);
        Assert.Contains("command = \"dotnet\"", toml);
        Assert.Contains("exec", toml);
        Assert.Contains(ClientConfigBuilder.RelativeReleaseHostDll, toml);
    }

    [Fact]
    public void DoctorReport_format_includes_force_headless_note()
    {
        var report = new DoctorReport
        {
            BridgeListening = true,
            BridgePort = 17890,
            BridgeStatus = "Listening 127.0.0.1:17890",
            LastClientCallUtc = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc),
            LastMethod = "ping",
            HostDllExists = false,
            ExpectedHostDllPath = "C:/proj/" + ClientConfigBuilder.RelativeReleaseHostDll,
            ProjectLocalConfigPresent = new Dictionary<McpClientKind, bool>
            {
                [McpClientKind.Cursor] = true,
                [McpClientKind.VsCode] = false,
                [McpClientKind.ClaudeCode] = false,
                [McpClientKind.CodexCli] = false
            }
        };
        var text = report.FormatText();
        Assert.Contains("Bridge listening: yes", text);
        Assert.Contains("17890", text);
        Assert.Contains("method=ping", text);
        Assert.Contains("UNITY_COMDR_FORCE_HEADLESS", text);
        Assert.Contains(".cursor/mcp.json: present", text);
        Assert.Contains("MISSING", text);
    }

    [Fact]
    public void Cursor_deeplink_config_is_standard_base64_utf8()
    {
        var json = ClientConfigBuilder.BuildStdioServerConfigJson("a/b.dll");
        var url = ClientConfigBuilder.BuildCursorDeeplink("a/b.dll");
        var q = new Uri(url).Query.TrimStart('?');
        var configPart = q.Split('&').First(p => p.StartsWith("config=", StringComparison.OrdinalIgnoreCase));
        var b64 = Uri.UnescapeDataString(configPart.Substring("config=".Length));
        Assert.Equal(json, Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
    }
}
