namespace UnityComdr.Models;

public enum LogType
{
    Log,
    Warning,
    Error
}

/// <param name="Epoch">Compile epoch when recorded (O1 / FR-A6).</param>
/// <param name="Stale">True when <paramref name="Epoch"/> is older than the current compile epoch.</param>
public sealed record ConsoleLogEntry(
    LogType Type,
    string Message,
    string? StackTrace = null,
    string? File = null,
    int Line = 0,
    int Epoch = 0,
    bool Stale = false);

public sealed record Vector3(float X, float Y, float Z)
{
    public static Vector3 Zero => new(0, 0, 0);
    public static Vector3 One => new(1, 1, 1);
}

public sealed record TransformData(
    Vector3 Position,
    Vector3 RotationEuler,
    Vector3 Scale);

public sealed class ComponentData
{
    public string TypeName { get; set; } = "";
    public Dictionary<string, object?> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GameObjectData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "GameObject";
    public string? ParentId { get; set; }
    public bool Active { get; set; } = true;
    public string Tag { get; set; } = "Untagged";
    public int Layer { get; set; }
    public TransformData Transform { get; set; } = new(Vector3.Zero, Vector3.Zero, Vector3.One);
    public List<ComponentData> Components { get; set; } = new();
    public List<string> ChildIds { get; set; } = new();
}

public sealed class SceneData
{
    public string Path { get; set; } = "Assets/Scenes/Untitled.unity";
    public string Name { get; set; } = "Untitled";
    public bool Dirty { get; set; }
    public bool IsLoaded { get; set; } = true;
    public List<string> RootObjectIds { get; set; } = new();
}

public sealed class ScriptFile
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
}

public sealed class AssetRecord
{
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "Unknown"; // Script, Material, Prefab, Other
    public string? MaterialColor { get; set; }
    public string? PrefabSourceObjectId { get; set; }
}

public sealed class EditorState
{
    public bool IsCompiling { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsPaused { get; set; }
    public string ActiveScenePath { get; set; } = "Assets/Scenes/Untitled.unity";

    /// <summary>
    /// Machine-readable lifecycle: connected | editor_compiling | editor_reloading | play_transition | editor_gone.
    /// </summary>
    public string Phase { get; set; } = "connected";

    /// <summary>Suggested client retry delay when <see cref="Phase"/> is busy; null when connected.</summary>
    public int? SuggestedRetrySeconds { get; set; }

    /// <summary>Monotonic compile generation; bumped by <c>editor_compile</c> (O1 / FR-A6).</summary>
    public int CompileEpoch { get; set; }

    /// <summary>Monotonic domain-reload generation; invalidates prior instance ids (O2 / FR-A5).</summary>
    public int SessionGeneration { get; set; } = 1;

    /// <summary>
    /// Host adapter: <c>live</c> = Unity TCP bridge, <c>headless</c> = InMemoryEditorHost.
    /// Agent-visible so silent headless is never mistaken for live Editor control.
    /// </summary>
    public string HostMode { get; set; } = "headless";

    /// <summary>Optional human-readable host selection detail from <c>EditorHostFactory</c>.</summary>
    public string? HostDetail { get; set; }
}

/// <summary>Async test job snapshot (TestRunnerApi on live; unsupported on headless).</summary>
public sealed class TestJobSnapshot
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = ""; // running | completed | failed | unsupported
    public string Mode { get; set; } = "EditMode";
    public string? Filter { get; set; }
    public bool? Passed { get; set; }
    public List<TestCaseResultRow> Results { get; set; } = new();
    public string? Note { get; set; }
}

public sealed class TestCaseResultRow
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Message { get; set; }
}

public sealed class TestCatalogEntry
{
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "EditMode";
}

public sealed class MaterialData
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF";
    public string Shader { get; set; } = "Standard";
}

public sealed class PrefabData
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceObjectId { get; set; } = "";
}
