using System.Text;
using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.McpHost;
using Xunit;

namespace UnityComdr.Tests;

public class McpProtocolTests
{
    [Fact]
    public async Task Initialize_and_tools_list_match_default_core_set()
    {
        var (server, output) = CreateServer();
        var init = await server.HandleLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}""");
        Assert.NotNull(init);
        Assert.Equal("unity-comdr", init!["result"]?["serverInfo"]?["name"]?.GetValue<string>());

        var list = await server.HandleLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
        Assert.NotNull(list);
        var tools = list!["result"]?["tools"] as JsonArray;
        Assert.NotNull(tools);
        Assert.True(tools!.Count <= Tools.ToolBudget.MaxDefaultCoreTools);
        var names = tools.Select(t => t?["name"]?.GetValue<string>()).Where(n => n != null).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("console_read", names);
        Assert.Contains("script_write", names);
        Assert.Contains("scene_manage", names);
        Assert.Contains("gameobject_manage", names);
        Assert.Contains("skill_manage", names);
        Assert.DoesNotContain("tests_run", names);
        Assert.DoesNotContain("reflect_call", names);
        Assert.True(output.ToString().Length > 0);
    }

    [Fact]
    public async Task Tools_call_console_read_returns_mcp_content()
    {
        var runtime = new ComdrRuntime();
        runtime.Editor.AddConsoleLog(new Models.ConsoleLogEntry(Models.LogType.Error, "injected-error-xyz"));
        var server = new McpServer(runtime, new StringReader(""), new StringWriter());

        var call = await server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"console_read","arguments":{"type":"Error"}}}""");
        Assert.NotNull(call);
        Assert.Null(call!["error"]);
        var text = call["result"]?["content"]?[0]?["text"]?.GetValue<string>();
        Assert.NotNull(text);
        Assert.Contains("injected-error-xyz", text);
        Assert.False(call["result"]?["isError"]?.GetValue<bool>() ?? true);
    }

    [Fact]
    public async Task Double_launch_initialize_tools_list_consistent()
    {
        for (var run = 0; run < 2; run++)
        {
            var (server, _) = CreateServer();
            var init = await server.HandleLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
            Assert.NotNull(init?["result"]);
            var list = await server.HandleLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var tools = list!["result"]?["tools"] as JsonArray;
            Assert.NotNull(tools);
            Assert.InRange(tools!.Count, 1, Tools.ToolBudget.MaxDefaultCoreTools);
            Assert.Contains(tools, t => t?["name"]?.GetValue<string>() == "console_read");
        }
    }

    private static (McpServer server, StringWriter output) CreateServer()
    {
        var runtime = new ComdrRuntime();
        var output = new StringWriter();
        var server = new McpServer(runtime, new StringReader(""), output);
        return (server, output);
    }
}
