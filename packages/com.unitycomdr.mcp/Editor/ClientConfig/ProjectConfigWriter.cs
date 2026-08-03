#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace UnityComdr.ClientConfig
{
    /// <summary>
    /// Writes project-local MCP client configs (FR-I2). Pattern from Coplay JsonFileMcpConfigurator
    /// + CoderGamester project-relative auto-config — no Python required.
    /// </summary>
    public static class ProjectConfigWriter
    {
        public static string GetUnityProjectRoot()
        {
            var dataPath = Application.dataPath.Replace('\\', '/');
            return Path.GetFullPath(dataPath + "/..");
        }

        public static string ResolveAbsoluteHostDll()
        {
            return ClientConfigBuilder.ResolveHostDllPath(GetUnityProjectRoot(), preferRelease: true);
        }

        public static string ResolveConfigHostPath()
        {
            var root = GetUnityProjectRoot();
            var abs = ResolveAbsoluteHostDll();
            return ClientConfigBuilder.ToConfigHostPath(root, abs);
        }

        /// <summary>Write/overwrite project-local config for <paramref name="kind"/>. Returns absolute path written.</summary>
        public static string WriteProjectLocal(McpClientKind kind)
        {
            var root = GetUnityProjectRoot();
            var rel = ClientConfigBuilder.GetProjectLocalConfigRelativePath(kind);
            var absPath = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            var dir = Path.GetDirectoryName(absPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var hostForConfig = ResolveConfigHostPath();
            var content = ClientConfigBuilder.BuildProjectLocalConfigContent(kind, hostForConfig);
            File.WriteAllText(absPath, content);
            return absPath;
        }

        public static bool ProjectLocalExists(McpClientKind kind)
        {
            var root = GetUnityProjectRoot();
            var rel = ClientConfigBuilder.GetProjectLocalConfigRelativePath(kind);
            var absPath = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(absPath);
        }

        public static bool TryDetectUserConfigDir(McpClientKind kind, out string path)
        {
            path = null;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            switch (kind)
            {
                case McpClientKind.Cursor:
                    path = Path.Combine(home, ".cursor");
                    break;
                case McpClientKind.VsCode:
                    path = Path.Combine(appData, "Code", "User");
                    break;
                case McpClientKind.ClaudeCode:
                    path = Path.Combine(home, ".claude");
                    break;
                case McpClientKind.CodexCli:
                    path = Path.Combine(home, ".codex");
                    break;
                default:
                    return false;
            }
            return !string.IsNullOrEmpty(path) && Directory.Exists(path);
        }
    }
}
#endif
