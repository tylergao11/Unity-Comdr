namespace UnityComdr.Models;

/// <summary>Screen-space axis-aligned rect (pixels, top-left origin for headless; Unity screen space for live).</summary>
public sealed class UiRect
{
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
}

/// <summary>Interactable UI control summary for observe.ui / ui_query.</summary>
public sealed class UiControlInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public UiRect Rect { get; set; } = new();
    public bool Interactable { get; set; } = true;
    public string? Path { get; set; }
    public string? Kind { get; set; }
}

/// <summary>Task-level lease snapshot (DESIGN §5.3).</summary>
public sealed class LeaseInfo
{
    public string? Holder { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool Held => !string.IsNullOrEmpty(Holder);
}

/// <summary>Write authorization against the task lease.</summary>
public sealed class LeaseAuthorization
{
    public bool Allowed { get; init; }
    public string Status { get; init; } = "ok"; // ok | busy | missing_lease | not_holder
    public string? Holder { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    public static LeaseAuthorization Ok(string? holder = null, DateTimeOffset? expiresAt = null) =>
        new() { Allowed = true, Status = "ok", Holder = holder, ExpiresAt = expiresAt };

    public static LeaseAuthorization Busy(string? holder, DateTimeOffset? expiresAt = null) =>
        new() { Allowed = false, Status = "busy", Holder = holder, ExpiresAt = expiresAt };

    public static LeaseAuthorization MissingLease() =>
        new() { Allowed = false, Status = "missing_lease", Holder = null };
}

/// <summary>Result of a simulated input action.</summary>
public sealed class InputSimulateResult
{
    public bool Ok { get; set; }
    public string Action { get; set; } = "";
    public string? Target { get; set; }
    public string? Note { get; set; }
    public Dictionary<string, object?> Effects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
