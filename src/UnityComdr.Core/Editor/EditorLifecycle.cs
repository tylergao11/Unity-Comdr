namespace UnityComdr.Editor;

/// <summary>
/// Machine-readable Editor lifecycle phases (FR-R1 / PR-5).
/// Agents must retry on busy phases — never treat silence or fake success as done.
/// </summary>
public static class EditorLifecyclePhases
{
    public const string Connected = "connected";
    public const string EditorCompiling = "editor_compiling";
    public const string EditorReloading = "editor_reloading";
    public const string PlayTransition = "play_transition";
    public const string EditorGone = "editor_gone";

    public static bool IsBusy(string? phase) =>
        !string.IsNullOrEmpty(phase) &&
        !string.Equals(phase, Connected, StringComparison.OrdinalIgnoreCase);

    public static int DefaultRetrySeconds(string phase) => phase.ToLowerInvariant() switch
    {
        EditorCompiling => 3,
        EditorReloading => 5,
        PlayTransition => 2,
        EditorGone => 5,
        _ => 3
    };

    public static string DefaultNextStep(string phase) => phase.ToLowerInvariant() switch
    {
        EditorCompiling =>
            "Wait for Unity compile to finish, then retry the same tool call.",
        EditorReloading =>
            "Wait for domain reload to finish, reconnect if needed, then retry.",
        PlayTransition =>
            "Wait for play-mode enter/exit to settle, then retry.",
        EditorGone =>
            "Ensure the Unity Editor is open with the live bridge running, then retry.",
        _ => "Retry after a short delay."
    };

    /// <summary>
    /// Stable busy error text: phase name + suggestedRetrySeconds + nextStep.
    /// Parsed by <see cref="EditorBusyException.TryParse"/>.
    /// </summary>
    public static string FormatBusyMessage(string phase, int? suggestedRetrySeconds = null, string? nextStep = null)
    {
        var retry = suggestedRetrySeconds ?? DefaultRetrySeconds(phase);
        var step = nextStep ?? DefaultNextStep(phase);
        return $"{phase} suggestedRetrySeconds={retry} nextStep={step}";
    }
}

/// <summary>
/// Thrown when the Editor cannot execute work during a transition (PR-5: immediate busy, no silent queue).
/// </summary>
public sealed class EditorBusyException : Exception
{
    public string Phase { get; }
    public int SuggestedRetrySeconds { get; }
    public string NextStep { get; }

    public EditorBusyException(string phase, int? suggestedRetrySeconds = null, string? nextStep = null)
        : base(EditorLifecyclePhases.FormatBusyMessage(phase, suggestedRetrySeconds, nextStep))
    {
        Phase = phase;
        SuggestedRetrySeconds = suggestedRetrySeconds ?? EditorLifecyclePhases.DefaultRetrySeconds(phase);
        NextStep = nextStep ?? EditorLifecyclePhases.DefaultNextStep(phase);
    }

    public static bool TryParse(string? error, out EditorBusyException? busy)
    {
        busy = null;
        if (string.IsNullOrWhiteSpace(error)) return false;
        foreach (var phase in new[]
                 {
                     EditorLifecyclePhases.EditorCompiling,
                     EditorLifecyclePhases.EditorReloading,
                     EditorLifecyclePhases.PlayTransition,
                     EditorLifecyclePhases.EditorGone
                 })
        {
            if (error.IndexOf(phase, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var retry = EditorLifecyclePhases.DefaultRetrySeconds(phase);
            const string marker = "suggestedRetrySeconds=";
            var idx = error.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var start = idx + marker.Length;
                var end = start;
                while (end < error.Length && char.IsDigit(error[end])) end++;
                if (end > start && int.TryParse(error.AsSpan(start, end - start), out var parsed))
                    retry = parsed;
            }
            busy = new EditorBusyException(phase, retry);
            return true;
        }
        return false;
    }
}
