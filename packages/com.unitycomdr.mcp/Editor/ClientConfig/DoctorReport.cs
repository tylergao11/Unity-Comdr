#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;

namespace UnityComdr.ClientConfig
{
    /// <summary>Keep in sync with src/UnityComdr.Core/ClientConfig/DoctorReport.cs</summary>
    public sealed class DoctorReport
    {
        public bool BridgeListening;
        public int BridgePort;
        public string BridgeStatus = "";
        public DateTime? LastClientCallUtc;
        public string LastMethod;
        public bool HostDllExists;
        public string ExpectedHostDllPath = "";
        public string ForceHeadlessEnvNote =
            "UNITY_COMDR_FORCE_HEADLESS=1 forces InMemoryEditorHost (skip live bridge). Leave unset for live Editor when bridge is up.";
        public Dictionary<McpClientKind, bool> ProjectLocalConfigPresent =
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
                bool present = ProjectLocalConfigPresent != null &&
                               ProjectLocalConfigPresent.TryGetValue(kind, out var p) && p;
                yield return "Project config " + ClientConfigBuilder.GetProjectLocalConfigRelativePath(kind) +
                             ": " + (present ? "present" : "absent");
            }
        }
    }
}
#endif
