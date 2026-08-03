#if UNITY_EDITOR
using UnityEngine;

namespace UnityComdr.ClientConfig
{
    /// <summary>Opens Cursor/VS Code MCP install deeplinks (FR-I1).</summary>
    public static class DeeplinkLauncher
    {
        public static bool TryOpen(McpClientKind kind, string hostDllPathForConfig, out string urlOrError)
        {
            if (!ClientConfigBuilder.SupportsDeeplink(kind))
            {
                urlOrError = kind + " does not support install deeplinks; use Write project config or Copy JSON.";
                return false;
            }

            var url = ClientConfigBuilder.BuildDeeplink(kind, hostDllPathForConfig);
            if (string.IsNullOrEmpty(url))
            {
                urlOrError = "Failed to build deeplink.";
                return false;
            }

            Application.OpenURL(url);
            urlOrError = url;
            return true;
        }
    }
}
#endif
