namespace UnityComdr.Models;

public sealed class PackageInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Source { get; set; } = "registry"; // registry | git | local | built-in
    public string? DisplayName { get; set; }
}

public sealed class SelectionState
{
    public List<string> GameObjectIds { get; set; } = new();
    public List<string> AssetPaths { get; set; } = new();
}

public sealed class MenuItemInfo
{
    public string Path { get; set; } = "";
    public string? Category { get; set; }
}

public sealed class ProfilerSnapshot
{
    public bool Enabled { get; set; }
    public float DeltaTimeMs { get; set; }
    public float Fps { get; set; }
    public long MonoUsedBytes { get; set; }
    public long TotalAllocatedBytes { get; set; }
    public List<string> EnabledModules { get; set; } = new();
}

public sealed class ScreenshotResult
{
    public string Source { get; set; } = ""; // camera | game_view | scene_view | isolated
    public string Format { get; set; } = "png";
    public string? Note { get; set; }
    public string? TargetId { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Inline PNG as base64 when <see cref="IsRealPixels"/> is true.</summary>
    public string? PngBase64 { get; set; }
    /// <summary>Optional on-disk path for the full PNG.</summary>
    public string? FilePath { get; set; }
    /// <summary>True only when real GPU/Editor pixels were captured. Headless must stay false.</summary>
    public bool IsRealPixels { get; set; }
    /// <summary>Whether Screen Space – Overlay UI was included (game_view composited path).</summary>
    public bool? OverlayUiIncluded { get; set; }
}

public sealed class FolderRecord
{
    public string Path { get; set; } = "";
}
