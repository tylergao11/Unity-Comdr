namespace UnityComdr.Editor;

/// <summary>
/// Selects the editor adapter for the MCP host process.
/// Prefers a live Unity bridge when reachable; otherwise headless InMemoryEditorHost.
/// Both implement the same <see cref="IEditorHost"/> used by all shipped handlers.
/// </summary>
public static class EditorHostFactory
{
    public const int DefaultLiveBridgePort = 17890;
    public const string EnvPort = "UNITY_COMDR_BRIDGE_PORT";
    public const string EnvForceHeadless = "UNITY_COMDR_FORCE_HEADLESS";

    public static EditorHostSelection CreateFromEnvironment()
    {
        if (IsTruthy(Environment.GetEnvironmentVariable(EnvForceHeadless)))
        {
            return new EditorHostSelection(
                new InMemoryEditorHost(),
                EditorHostMode.HeadlessInMemory,
                "UNITY_COMDR_FORCE_HEADLESS is set.");
        }

        var portText = Environment.GetEnvironmentVariable(EnvPort);
        var port = DefaultLiveBridgePort;
        if (!string.IsNullOrWhiteSpace(portText) && int.TryParse(portText, out var p) && p > 0)
            port = p;

        try
        {
            var live = new BridgeClientEditorHost(port);
            if (live.TryConnect(TimeSpan.FromMilliseconds(400)))
            {
                return new EditorHostSelection(
                    live,
                    EditorHostMode.LiveUnityBridge,
                    $"Connected to Unity live bridge on 127.0.0.1:{port}.");
            }
            live.Dispose();
        }
        catch (Exception ex)
        {
            return new EditorHostSelection(
                new InMemoryEditorHost(),
                EditorHostMode.HeadlessInMemory,
                $"Live bridge unavailable ({ex.GetType().Name}: {ex.Message}); using InMemoryEditorHost.");
        }

        return new EditorHostSelection(
            new InMemoryEditorHost(),
            EditorHostMode.HeadlessInMemory,
            $"No Unity live bridge on 127.0.0.1:{port}; using InMemoryEditorHost.");
    }

    private static bool IsTruthy(string? v) =>
        !string.IsNullOrWhiteSpace(v) &&
        (v.Equals("1", StringComparison.OrdinalIgnoreCase) ||
         v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
         v.Equals("yes", StringComparison.OrdinalIgnoreCase));
}

public enum EditorHostMode
{
    HeadlessInMemory,
    LiveUnityBridge
}

public sealed record EditorHostSelection(IEditorHost Host, EditorHostMode Mode, string Detail);
