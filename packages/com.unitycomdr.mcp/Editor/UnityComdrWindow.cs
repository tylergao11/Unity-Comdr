#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityComdr.ClientConfig;
using UnityEditor;
using UnityEngine;

namespace UnityComdr.UnityEditor
{
    /// <summary>
    /// Editor status + multi-client MCP configuration (FR-I1/I2/I3) + Trust (FR-T1/T2/T3).
    /// Pattern port from Coplay Clients/Setup/Windows — deeplink, project-local write, doctor, copy-JSON.
    /// </summary>
    public sealed class UnityComdrWindow : EditorWindow
    {
        private string _hostPath = "";
        private string _configHostPath = "";
        private Vector2 _scroll;
        private int _clientIndex;
        private bool _doctorFoldout = true;
        private bool _trustFoldout = true;
        private string _lastActionMessage = "";
        private string _disabledToolsText = "";
        private string _disabledSkillsText = "";
        private bool _auditEnabled = true;

        private static readonly string[] ClientLabels =
        {
            "Cursor",
            "VS Code Copilot",
            "Claude Code",
            "Codex CLI (toml)"
        };

        private static readonly McpClientKind[] ClientKinds =
        {
            McpClientKind.Cursor,
            McpClientKind.VsCode,
            McpClientKind.ClaudeCode,
            McpClientKind.CodexCli
        };

        [MenuItem("Window/Unity-Comdr MCP")]
        public static void Open()
        {
            var w = GetWindow<UnityComdrWindow>("Unity-Comdr MCP");
            w.minSize = new Vector2(480, 520);
        }

        private void OnEnable()
        {
            RefreshHostPaths();
            ReloadTrustUi();
        }

        private void RefreshHostPaths()
        {
            _hostPath = ProjectConfigWriter.ResolveAbsoluteHostDll();
            _configHostPath = ProjectConfigWriter.ResolveConfigHostPath();
        }

        private void ReloadTrustUi()
        {
            var cfg = BridgeTrust.LoadConfig();
            _disabledToolsText = string.Join(", ", cfg.DisabledTools);
            _disabledSkillsText = string.Join(", ", cfg.DisabledSkills);
            _auditEnabled = cfg.AuditEnabled;
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Unity-Comdr MCP", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Local-first MCP: no Python, Node, or cloud account required.\n" +
                "First 5 minutes: deeplink (Cursor/VS Code) or Write project config, then Copy JSON fallback.\n" +
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
            EditorGUILayout.SelectableLabel(_hostPath, EditorStyles.textField, GUILayout.Height(36));
            EditorGUILayout.LabelField("Path written into configs (relative when possible)", EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel(_configHostPath, EditorStyles.textField, GUILayout.Height(28));
            if (GUILayout.Button("Refresh host path", GUILayout.Width(140)))
                RefreshHostPaths();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Configure MCP client", EditorStyles.boldLabel);
            _clientIndex = EditorGUILayout.Popup("Client", _clientIndex, ClientLabels);
            var kind = ClientKinds[Mathf.Clamp(_clientIndex, 0, ClientKinds.Length - 1)];

            string detectPath;
            if (ProjectConfigWriter.TryDetectUserConfigDir(kind, out detectPath))
                DrawStatus("Client install dir", "Detected: " + detectPath);
            else
                DrawStatus("Client install dir", "Not detected (still OK — write project config or copy JSON)");

            DrawStatus("Project config", ProjectConfigWriter.ProjectLocalExists(kind)
                ? "Present: " + ClientConfigBuilder.GetProjectLocalConfigRelativePath(kind)
                : "Absent: " + ClientConfigBuilder.GetProjectLocalConfigRelativePath(kind));

            EditorGUI.BeginDisabledGroup(!ClientConfigBuilder.SupportsDeeplink(kind));
            if (GUILayout.Button("Open install deeplink (Cursor / VS Code)"))
            {
                RefreshHostPaths();
                if (DeeplinkLauncher.TryOpen(kind, _configHostPath, out var urlOrError))
                {
                    _lastActionMessage = "Opened deeplink:\n" + urlOrError;
                    Debug.Log("[Unity-Comdr] Deeplink opened:\n" + urlOrError);
                }
                else
                {
                    _lastActionMessage = urlOrError;
                    EditorUtility.DisplayDialog("Unity-Comdr", urlOrError, "OK");
                }
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Write project-local config (relative path)"))
            {
                RefreshHostPaths();
                try
                {
                    var written = ProjectConfigWriter.WriteProjectLocal(kind);
                    _lastActionMessage = "Wrote " + written + "\nHost path in file: " + _configHostPath;
                    Debug.Log("[Unity-Comdr] Wrote project config: " + written);
                    EditorUtility.DisplayDialog("Unity-Comdr", "Wrote:\n" + written, "OK");
                }
                catch (System.Exception ex)
                {
                    _lastActionMessage = "Write failed: " + ex.Message;
                    EditorUtility.DisplayDialog("Unity-Comdr", _lastActionMessage, "OK");
                }
            }

            if (GUILayout.Button("Copy configuration for selected client"))
            {
                RefreshHostPaths();
                var snippet = ClientConfigBuilder.BuildProjectLocalConfigContent(kind, _configHostPath);
                EditorGUIUtility.systemCopyBuffer = snippet;
                _lastActionMessage = "Copied config for " + ClientLabels[_clientIndex];
                Debug.Log("[Unity-Comdr] Client config copied:\n" + snippet);
            }

            if (GUILayout.Button("Copy ALL client templates"))
            {
                RefreshHostPaths();
                var sb = new StringBuilder();
                for (var i = 0; i < ClientKinds.Length; i++)
                {
                    sb.AppendLine("===== " + ClientLabels[i] + " =====");
                    sb.AppendLine(ClientConfigBuilder.BuildProjectLocalConfigContent(ClientKinds[i], _configHostPath));
                    sb.AppendLine();
                }
                EditorGUIUtility.systemCopyBuffer = sb.ToString();
                _lastActionMessage = "Copied all client templates.";
                Debug.Log("[Unity-Comdr] All client templates copied to clipboard.");
            }

            if (ClientConfigBuilder.SupportsDeeplink(kind) && GUILayout.Button("Copy deeplink URL"))
            {
                RefreshHostPaths();
                var url = ClientConfigBuilder.BuildDeeplink(kind, _configHostPath);
                EditorGUIUtility.systemCopyBuffer = url ?? "";
                _lastActionMessage = "Copied deeplink URL.";
            }

            if (!string.IsNullOrEmpty(_lastActionMessage))
                EditorGUILayout.HelpBox(_lastActionMessage, MessageType.None);

            EditorGUILayout.Space(6);
            _doctorFoldout = EditorGUILayout.Foldout(_doctorFoldout, "Doctor (self-test)", true);
            if (_doctorFoldout)
            {
                RefreshHostPaths();
                var report = DoctorChecks.Run();
                foreach (var line in report.FormatLines())
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Copy doctor report"))
                {
                    EditorGUIUtility.systemCopyBuffer = report.FormatText();
                    _lastActionMessage = "Doctor report copied.";
                }
            }

            EditorGUILayout.Space(6);
            _trustFoldout = EditorGUILayout.Foldout(_trustFoldout, "Trust (FR-T1/T2/T3)", true);
            if (_trustFoldout)
            {
                DrawStatus("Bridge consent", BridgeTrust.IsConsentApproved ? "Approved (EditorPrefs)" : "Not approved — first tool call will prompt");
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Approve now", GUILayout.Width(110)))
                {
                    BridgeTrust.SetConsentApproved(true);
                    _lastActionMessage = "Bridge consent approved.";
                }
                if (GUILayout.Button("Revoke consent", GUILayout.Width(120)))
                {
                    BridgeTrust.RevokeConsent();
                    _lastActionMessage = "Bridge consent revoked. Next tool method will prompt again.";
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Disable lists (ProjectSettings/UnityComdr.mcp.json)", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("Disabled tools (comma-separated MCP tool names)", EditorStyles.miniLabel);
                _disabledToolsText = EditorGUILayout.TextField(_disabledToolsText);
                EditorGUILayout.LabelField("Disabled skills (comma-separated skill ids)", EditorStyles.miniLabel);
                _disabledSkillsText = EditorGUILayout.TextField(_disabledSkillsText);
                _auditEnabled = EditorGUILayout.Toggle("Local audit log enabled", _auditEnabled);
                DrawStatus("Audit log", BridgeTrust.GetAuditLogPath());

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Save trust settings", GUILayout.Width(140)))
                {
                    try
                    {
                        var cfg = new TrustConfig { AuditEnabled = _auditEnabled };
                        cfg.DisabledTools.AddRange(SplitCsv(_disabledToolsText));
                        cfg.DisabledSkills.AddRange(SplitCsv(_disabledSkillsText));
                        BridgeTrust.SaveConfig(cfg);
                        ReloadTrustUi();
                        _lastActionMessage = "Wrote " + BridgeTrust.GetSettingsPath();
                    }
                    catch (Exception ex)
                    {
                        _lastActionMessage = "Save trust settings failed: " + ex.Message;
                        EditorUtility.DisplayDialog("Unity-Comdr", _lastActionMessage, "OK");
                    }
                }
                if (GUILayout.Button("Reload", GUILayout.Width(80)))
                    ReloadTrustUi();
                if (GUILayout.Button("Reveal audit log", GUILayout.Width(120)))
                {
                    var p = BridgeTrust.GetAuditLogPath();
                    if (File.Exists(p))
                        EditorUtility.RevealInFinder(p);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(p) ?? ".");
                        EditorUtility.RevealInFinder(Path.GetDirectoryName(p));
                    }
                }
                EditorGUILayout.EndHorizontal();
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

        private static List<string> SplitCsv(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var part in text.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }
    }
}
#endif
