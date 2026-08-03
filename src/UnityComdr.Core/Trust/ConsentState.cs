namespace UnityComdr.Trust;

/// <summary>FR-T1 first-connection consent decision (pure state machine; Unity EditorPrefs persist separately).</summary>
public enum ConsentDecision
{
    /// <summary>No decision yet — tool methods must be refused until Approve.</summary>
    Unknown = 0,
    /// <summary>Approval dialog / prompt is in flight.</summary>
    Pending = 1,
    /// <summary>Operator approved local bridge/MCP control.</summary>
    Approved = 2,
    /// <summary>Operator denied; tool methods stay refused until Approve or revoke+re-prompt.</summary>
    Denied = 3
}

/// <summary>
/// Unit-testable consent state for FR-T1 (Unity official MCP pending-connection approval mode).
/// Doctor probes (ping / getState) remain allowed while tool methods require <see cref="ConsentDecision.Approved"/>.
/// </summary>
public sealed class ConsentState
{
    public ConsentDecision Decision { get; private set; } = ConsentDecision.Unknown;

    public bool AllowsToolMethods => Decision == ConsentDecision.Approved;

    /// <summary>Restore from persisted EditorPrefs / settings (true = previously approved).</summary>
    public void RestoreFromPersisted(bool approved)
    {
        Decision = approved ? ConsentDecision.Approved : ConsentDecision.Unknown;
    }

    public void MarkPending() => Decision = ConsentDecision.Pending;

    public void Approve() => Decision = ConsentDecision.Approved;

    public void Deny() => Decision = ConsentDecision.Denied;

    /// <summary>Clear remembered approval so the next tool call re-prompts.</summary>
    public void Revoke() => Decision = ConsentDecision.Unknown;

    /// <summary>
    /// Whether <paramref name="method"/> is a doctor/lifecycle probe that may run without consent.
    /// Accepts bridge methods (<c>ping</c>, <c>editor.getState</c>) and MCP tool name <c>editor_state</c>.
    /// </summary>
    public static bool IsDoctorMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method)) return false;
        return method.Equals("ping", StringComparison.OrdinalIgnoreCase)
               || method.Equals("editor.getState", StringComparison.OrdinalIgnoreCase)
               || method.Equals("editor_state", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns false with a clear error when tool methods are blocked.
    /// Doctor methods always pass. Approved always passes.
    /// </summary>
    public bool TryAuthorize(string? method, out string? error)
    {
        error = null;
        if (IsDoctorMethod(method))
            return true;
        if (Decision == ConsentDecision.Approved)
            return true;

        error = Decision == ConsentDecision.Denied
            ? "consent_denied: Bridge consent was denied in the Unity Editor. Open Window/Unity-Comdr MCP, approve the connection, then retry."
            : "consent_required: First external MCP/bridge connection must be approved in the Unity Editor before tool methods are allowed. Doctor probes (ping / editor.getState) remain available.";
        return false;
    }
}
