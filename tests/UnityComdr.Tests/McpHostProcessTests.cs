using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// Launches the real shipped host assembly over stdio (twice) and asserts MCP initialize/tools/list.
/// </summary>
public class McpHostProcessTests
{
    [Fact]
    public void Host_process_initialize_and_tools_list_twice()
    {
        var dll = FindHostDll();
        Assert.True(File.Exists(dll), $"Host DLL not found at {dll}. Build Release/Debug first.");

        for (var i = 0; i < 2; i++)
        {
            var (exit, stdout, stderr) = RunHost(dll,
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"proc-test","version":"0"}}}""",
                """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""",
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"console_read","arguments":{"pageSize":5}}}""");

            Assert.Equal(0, exit);
            Assert.Contains("unity-comdr", stdout);
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var listLine = lines.First(l => l.Contains("\"id\":2") || l.Contains("\"id\": 2"));
            var node = JsonNode.Parse(listLine) as JsonObject;
            Assert.NotNull(node);
            var tools = node!["result"]?["tools"] as JsonArray;
            Assert.NotNull(tools);
            Assert.InRange(tools!.Count, 1, Tools.ToolBudget.MaxDefaultCoreTools);
            var names = tools.Select(t => t?["name"]?.GetValue<string>()).ToHashSet();
            Assert.Contains("console_read", names);
            Assert.Contains("skill_manage", names);
            Assert.DoesNotContain("tests_run", names);
            Assert.DoesNotContain("reflect_call", names);
            Assert.Contains("unity-comdr mcp host starting", stderr);
        }
    }

    private static string FindHostDll()
    {
        var baseDir = AppContext.BaseDirectory;
        // tests/.../bin/Release/net8.0 -> repo root roughly
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "UnityComdr.McpHost", "bin", "Release", "net8.0", "UnityComdr.McpHost.dll")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "UnityComdr.McpHost", "bin", "Debug", "net8.0", "UnityComdr.McpHost.dll")),
            Path.GetFullPath(Path.Combine(baseDir, "UnityComdr.McpHost.dll"))
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static (int exit, string stdout, string stderr) RunHost(string dll, params string[] requestLines)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"exec \"{dll}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start host");
        foreach (var line in requestLines)
            p.StandardInput.WriteLine(line);
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(20000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException("Host did not exit in time");
        }
        return (p.ExitCode, stdout, stderr);
    }
}
