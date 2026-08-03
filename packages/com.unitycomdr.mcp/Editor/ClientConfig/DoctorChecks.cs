#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityComdr.UnityEditor;

namespace UnityComdr.ClientConfig
{
    /// <summary>FR-I3 doctor surface — bridge, port, last call, host DLL, FORCE_HEADLESS note.</summary>
    public static class DoctorChecks
    {
        public static DoctorReport Run()
        {
            var root = ProjectConfigWriter.GetUnityProjectRoot();
            var expected = Path.GetFullPath(Path.Combine(
                root,
                ClientConfigBuilder.RelativeReleaseHostDll.Replace('/', Path.DirectorySeparatorChar)));

            var present = new Dictionary<McpClientKind, bool>();
            foreach (McpClientKind kind in System.Enum.GetValues(typeof(McpClientKind)))
                present[kind] = ProjectConfigWriter.ProjectLocalExists(kind);

            return new DoctorReport
            {
                BridgeListening = LiveUnityBridgeServer.IsRunning,
                BridgePort = LiveUnityBridgeServer.ListeningPort,
                BridgeStatus = LiveUnityBridgeServer.Status ?? "",
                LastClientCallUtc = LiveUnityBridgeServer.LastClientCallUtc,
                LastMethod = LiveUnityBridgeServer.LastMethod,
                HostDllExists = File.Exists(expected),
                ExpectedHostDllPath = ClientConfigBuilder.NormalizeSlashes(expected),
                ProjectLocalConfigPresent = present
            };
        }
    }
}
#endif
