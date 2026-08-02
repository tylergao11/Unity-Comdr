#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityComdr.UnityEditor
{
    /// <summary>
    /// Editor status + multi-client MCP configuration snippets (Coplay/CoderGamester UX parity goal).
    /// </summary>
    public sealed class UnityComdrWindow : EditorWindow
    {
        private string _hostPath = "";
        private Vector2 _scroll;
        private int _clientIndex;
        private static readonly string[] Clients =
        {
            "Cursor",
            "Claude Code / Desktop",
            "VS Code Copilot",
            "Windsurf",
            "Codex CLI (toml)"
        };

        [MenuItem("Window/Unity-Comdr MCP")]
        public static void Open()
        {
            var w = GetWindow<UnityComdrWindow>("Unity-Comdr MCP");
            w.minSize = new Vector2(480, 420);
        }

        private void OnEnable()
        {
            var dataPath = Application.dataPath.Replace('\\', '/');
            var projectRoot = Path.GetFullPath(dataPath + "/..");
            var release = Path.Combine(projectRoot, "src", "UnityComdr.McpHost", "bin", "Release", "net8.0", "UnityComdr.McpHost.dll");
            var debug = Path.Combine(projectRoot, "src", "UnityComdr.McpHost", "bin", "Debug", "net8.0", "UnityComdr.McpHost.dll");
            _hostPath = File.Exists(release) ? release : debug;
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Unity-Comdr MCP", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Local-first MCP: no Python, Node, or cloud account required.\n" +
                "Default ≤15 core tools; load skills (playmode, packages, menu, profiling…) on demand.\n" +
                "Build host: dotnet build -c Release  |  Test: dotnet test",
                MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Bridge status", EditorStyles.boldLabel);
            DrawStatus("Editor package", "Installed (com.unitycomdr.mcp)");
            DrawStatus("Host process", File.Exists(_hostPath) ? "DLL found" : "Build host first");
            DrawStatus("Live bridge", LiveUnityBridgeServer.IsRunning ? LiveUnityBridgeServer.Status : "Stopped (auto-starts on load)");
            DrawStatus("Cloud", "Not required");
            DrawStatus("Escape hatches", "Disabled by default");
            DrawStatus("Token model", "Core + on-demand skills");
            DrawStatus("Full loops", "code-fix · scene-build · playmode-verify");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Host path", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(_hostPath, EditorStyles.textField, GUILayout.Height(40));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Configure MCP client", EditorStyles.boldLabel);
            _clientIndex = EditorGUILayout.Popup("Client", _clientIndex, Clients);
            if (GUILayout.Button("Copy configuration for selected client"))
            {
                var snippet = BuildSnippet(Clients[_clientIndex], _hostPath);
                EditorGUIUtility.systemCopyBuffer = snippet;
                Debug.Log("[Unity-Comdr] Client config copied:\n" + snippet);
            }
            if (GUILayout.Button("Copy ALL client templates"))
            {
                var sb = new StringBuilder();
                foreach (var c in Clients)
                {
                    sb.AppendLine("===== " + c + " =====");
                    sb.AppendLine(BuildSnippet(c, _hostPath));
                    sb.AppendLine();
                }
                EditorGUIUtility.systemCopyBuffer = sb.ToString();
                Debug.Log("[Unity-Comdr] All client templates copied to clipboard.");
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Skills (load via skill_manage)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "testing · prefab-advanced · playmode · selection · packages · menu · profiling · screenshots · batch\n" +
                "Resources: unity://hierarchy, unity://console, unity://skills, …\n" +
                "Prompts: code_fix_loop, scene_build_loop, playmode_verify_loop, skill_expansion",
                MessageType.None);

            if (GUILayout.Button("Reveal README"))
            {
                var readme = Path.GetFullPath(Application.dataPath + "/../README.md");
                if (File.Exists(readme))
                    EditorUtility.RevealInFinder(readme);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawStatus(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(120));
            EditorGUILayout.LabelField(value);
            EditorGUILayout.EndHorizontal();
        }

        private static string BuildSnippet(string client, string hostPath)
        {
            var path = hostPath.Replace("\\", "/");
            if (client.StartsWith("Codex"))
            {
                return
                    "[mcp_servers.unity-comdr]\n" +
                    "command = \"dotnet\"\n" +
                    "args = [\"exec\", \"" + path + "\"]\n";
            }

            // Cursor / Claude / VS Code / Windsurf share JSON shape (minor key differences ignored for copy-paste)
            return
                "{\n" +
                "  \"mcpServers\": {\n" +
                "    \"unity-comdr\": {\n" +
                "      \"command\": \"dotnet\",\n" +
                "      \"args\": [\"exec\", \"" + path + "\"]\n" +
                "    }\n" +
                "  }\n" +
                "}\n";
        }
    }
}
#endif
