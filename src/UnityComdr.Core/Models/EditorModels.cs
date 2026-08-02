namespace UnityComdr.Models;

public enum LogType
{
    Log,
    Warning,
    Error
}

public sealed record ConsoleLogEntry(
    LogType Type,
    string Message,
    string? StackTrace = null,
    string? File = null,
    int Line = 0);

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
