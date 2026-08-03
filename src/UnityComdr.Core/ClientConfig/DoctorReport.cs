using System.Text;

namespace UnityComdr.ClientConfig;

/// <summary>
/// Pure model for FR-I3 doctor checks. Editor fills from LiveUnityBridgeServer + filesystem;
/// tests can construct directly without UnityEditor.
/// </summary>
public sealed class DoctorReport
{
    public bool BridgeListening { get; set; }
    public int BridgePort { get; set; }
    public string BridgeStatus { get; set; } = "";
    public DateTime? LastClientCallUtc { get; set; }
    public string? LastMethod { get; set; }
    public bool HostDllExists { get; set; }
    public string ExpectedHostDllPath { get; set; } = "";
    public string ForceHeadlessEnvNote { get; set; } =
        "UNITY_COMDR_FORCE_HEADLESS=1 forces InMemoryEditorHost (skip live bridge). Leave unset for live Editor when bridge is up.";
    public Dictionary<McpClientKind, bool> ProjectLocalConfigPresent { get; set; } =
        new Dictionary<McpClientKind, bool>();

    public string FormatText()
    {
        var sb = new StringBuilder();
        foreach (var line in FormatLines())
            sb.AppendLine(line);
        return sb.ToString();
    }

    public IEnumerable<string> FormatLines()
    {
        yield return "Bridge listening: " + (BridgeListening ? "yes" : "no");
        yield return "Bridge port: " + BridgePort +
                     (string.IsNullOrEmpty(BridgeStatus) ? "" : " (" + BridgeStatus + ")");
        if (LastClientCallUtc.HasValue)
        {
            yield return "Last client call (UTC): " + LastClientCallUtc.Value.ToString("o") +
                         (string.IsNullOrEmpty(LastMethod) ? "" : " method=" + LastMethod);
        }
        else
        {
            yield return "Last client call (UTC): none yet";
        }

        yield return "Host DLL (Release path): " + (HostDllExists ? "found" : "MISSING") +
                     " @ " + ExpectedHostDllPath;
        yield return "Env note: " + ForceHeadlessEnvNote;

        foreach (McpClientKind kind in Enum.GetValues(typeof(McpClientKind)))
        {
            var present = ProjectLocalConfigPresent != null &&
                          ProjectLocalConfigPresent.TryGetValue(kind, out var p) && p;
            yield return "Project config " + ClientConfigBuilder.GetProjectLocalConfigRelativePath(kind) +
                         ": " + (present ? "present" : "absent");
        }
    }
}
