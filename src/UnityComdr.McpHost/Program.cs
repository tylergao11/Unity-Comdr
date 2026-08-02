using UnityComdr.Bootstrap;
using UnityComdr.Editor;
using UnityComdr.McpHost;

// Unity-Comdr MCP host — stdio JSON-RPC, no Python/Node required.
// Prefers live Unity TCP bridge when Editor package is listening; else InMemoryEditorHost.
// Env: UNITY_COMDR_FORCE_HEADLESS=1 | UNITY_COMDR_BRIDGE_PORT=17890

var httpPort = ParseHttpPort(args);
if (httpPort != null)
{
    Console.Error.WriteLine($"HTTP mode on port {httpPort} is reserved for a future release; use stdio.");
    return 2;
}

var selection = EditorHostFactory.CreateFromEnvironment();
var runtime = new ComdrRuntime(selection.Host);
using var input = new StreamReader(Console.OpenStandardInput(), leaveOpen: true);
using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

Console.Error.WriteLine(
    $"unity-comdr mcp host starting mode={selection.Mode} coreTools={runtime.Registry.CoreToolCount} detail={selection.Detail}");

var server = new McpServer(runtime, input, output, Console.Error);
await server.RunAsync();

if (selection.Host is IDisposable d)
    d.Dispose();

return 0;

static int? ParseHttpPort(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] is "--http" or "-h" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
            return p;
    }
    return null;
}
