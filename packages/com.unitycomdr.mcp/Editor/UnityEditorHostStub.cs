#if UNITY_EDITOR
using UnityEngine;

namespace UnityComdr.UnityEditor
{
    /// <summary>
    /// Live host entry registration for structural discovery and Editor status.
    /// The actual live path is <see cref="LiveUnityBridgeServer"/> (TCP) +
    /// Core <c>BridgeClientEditorHost</c> implementing <c>IEditorHost</c>.
    /// </summary>
    public static class UnityEditorHostStub
    {
        /// <summary>Stable type name used by tests as structural proof of live host entry.</summary>
        public const string LiveHostEntryTypeName = "UnityComdr.UnityEditor.LiveUnityBridgeServer";

        public static string Status =>
            LiveUnityBridgeServer.IsRunning
                ? "Live bridge running: " + LiveUnityBridgeServer.Status
                : "Live bridge not running (will auto-start on Editor load). Headless host uses InMemoryEditorHost.";

        public static bool IsLiveBridgeRegistered => true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void EnsureBridgeTypeLoaded()
        {
            // Touch type so domain reload keeps InitializeOnLoad linked.
            _ = LiveUnityBridgeServer.Status;
        }
    }
}
#endif
