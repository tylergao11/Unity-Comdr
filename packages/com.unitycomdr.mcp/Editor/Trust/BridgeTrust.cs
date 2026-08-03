#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityComdr.UnityEditor
{
    /// <summary>
    /// FR-T1/T2/T3 Editor-side trust: first-connection consent (EditorPrefs),
    /// ProjectSettings/UnityComdr.mcp.json disable lists, local audit JSONL.
    /// Mode port of Unity official MCP pending-connection approval (no proprietary code).
    /// </summary>
    public static class BridgeTrust
    {
        public const string ConsentPrefsKey = "UnityComdr.BridgeConsentApproved";
        public const string RelativeSettingsPath = "ProjectSettings/UnityComdr.mcp.json";
        public const string AuditFileName = "unity-comdr-audit.jsonl";

        private static bool _promptInFlight;

        public static bool IsConsentApproved => EditorPrefs.GetBool(ConsentPrefsKey, false);

        public static void SetConsentApproved(bool approved) =>
            EditorPrefs.SetBool(ConsentPrefsKey, approved);

        public static void RevokeConsent() => EditorPrefs.DeleteKey(ConsentPrefsKey);

        public static bool IsDoctorMethod(string method)
        {
            if (string.IsNullOrEmpty(method)) return false;
            return string.Equals(method, "ping", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(method, "editor.getState", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Blocking consent for first tool method. Doctor probes always pass.
        /// When already approved via EditorPrefs, returns true without a dialog.
        /// </summary>
        public static bool EnsureConsent(string method, out string error)
        {
            error = null;
            if (IsDoctorMethod(method))
                return true;
            if (IsConsentApproved)
                return true;

            if (_promptInFlight)
            {
                error = "consent_required: Consent dialog already open in the Unity Editor. Approve or deny, then retry.";
                return false;
            }

            _promptInFlight = true;
            try
            {
                var approved = EditorUtility.DisplayDialog(
                    "Unity-Comdr — Bridge consent",
                    "An external MCP/bridge client on localhost wants to control this Unity Editor.\n\n" +
                    "Approve once to remember (EditorPrefs). Deny keeps doctor probes (ping / editor.getState) available but blocks all other bridge methods.\n\n" +
                    "You can revoke approval later from Window → Unity-Comdr MCP.",
                    "Approve",
                    "Deny");
                if (approved)
                {
                    SetConsentApproved(true);
                    Debug.Log("[Unity-Comdr] Bridge consent approved (remembered in EditorPrefs).");
                    return true;
                }

                SetConsentApproved(false);
                error = "consent_denied: First-connection consent was denied in the Unity Editor. Open Window/Unity-Comdr MCP to approve, then retry.";
                return false;
            }
            finally
            {
                _promptInFlight = false;
            }
        }

        public static string GetSettingsPath()
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
            return Path.Combine(root, "ProjectSettings", "UnityComdr.mcp.json");
        }

        public static string GetAuditLogPath()
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
            var temp = Path.Combine(root, "Temp");
            Directory.CreateDirectory(temp);
            return Path.Combine(temp, AuditFileName);
        }

        public static TrustConfig LoadConfig()
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
                return new TrustConfig();
            try
            {
                return TrustConfig.FromJson(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Unity-Comdr] Failed to read trust settings: " + ex.Message);
                return new TrustConfig();
            }
        }

        public static void SaveConfig(TrustConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            var path = GetSettingsPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, config.ToJson(), new UTF8Encoding(false));
            AssetDatabase.Refresh();
        }

        public static bool IsBridgeMethodDisabled(string method, TrustConfig config)
        {
            if (config == null || string.IsNullOrEmpty(method)) return false;
            // Bridge methods may be listed directly, or as MCP-style names if operators prefer that.
            if (config.IsDisabledTool(method)) return true;
            return false;
        }

        public static void AppendAudit(string toolName, bool ok, long durationMs, string error = null)
        {
            try
            {
                var cfg = LoadConfig();
                if (!cfg.AuditEnabled) return;

                var path = GetAuditLogPath();
                var sb = new StringBuilder(160);
                sb.Append("{\"timestamp\":\"").Append(DateTime.UtcNow.ToString("o")).Append("\",");
                sb.Append("\"tool\":").Append(JsonString(toolName)).Append(',');
                sb.Append("\"ok\":").Append(ok ? "true" : "false").Append(',');
                sb.Append("\"durationMs\":").Append(durationMs);
                if (!string.IsNullOrEmpty(error))
                    sb.Append(",\"error\":").Append(JsonString(Truncate(error, 240)));
                sb.Append('}');
                File.AppendAllText(path, sb.ToString() + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Unity-Comdr] audit append failed: " + ex.Message);
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

        private static string JsonString(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
        }
    }

    /// <summary>Minimal JSON trust config for Editor package (mirrors Core TrustSettings shape).</summary>
    public sealed class TrustConfig
    {
        public readonly List<string> DisabledTools = new List<string>();
        public readonly List<string> DisabledSkills = new List<string>();
        public bool AuditEnabled = true;

        public bool IsDisabledTool(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (var i = 0; i < DisabledTools.Count; i++)
            {
                if (string.Equals(DisabledTools[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public bool IsDisabledSkill(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (var i = 0; i < DisabledSkills.Count; i++)
            {
                if (string.Equals(DisabledSkills[i], id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.Append("  \"disabledTools\": ").Append(StringArray(DisabledTools)).AppendLine(",");
            sb.Append("  \"disabledSkills\": ").Append(StringArray(DisabledSkills)).AppendLine(",");
            sb.Append("  \"auditEnabled\": ").Append(AuditEnabled ? "true" : "false").AppendLine();
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static TrustConfig FromJson(string json)
        {
            var cfg = new TrustConfig();
            if (string.IsNullOrWhiteSpace(json)) return cfg;
            cfg.DisabledTools.AddRange(ExtractStringArray(json, "disabledTools"));
            cfg.DisabledSkills.AddRange(ExtractStringArray(json, "disabledSkills"));
            var auditIdx = json.IndexOf("\"auditEnabled\"", StringComparison.OrdinalIgnoreCase);
            if (auditIdx >= 0)
            {
                var colon = json.IndexOf(':', auditIdx);
                if (colon >= 0)
                {
                    var slice = json.Substring(colon + 1, Math.Min(16, json.Length - colon - 1));
                    if (slice.IndexOf("false", StringComparison.OrdinalIgnoreCase) >= 0)
                        cfg.AuditEnabled = false;
                }
            }
            return cfg;
        }

        private static string StringArray(List<string> items)
        {
            if (items == null || items.Count == 0) return "[]";
            var parts = new List<string>();
            foreach (var s in items)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                parts.Add("\"" + s.Trim().Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
            }
            return "[" + string.Join(", ", parts) + "]";
        }

        private static List<string> ExtractStringArray(string json, string key)
        {
            var result = new List<string>();
            var marker = "\"" + key + "\"";
            var idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return result;
            var start = json.IndexOf('[', idx);
            if (start < 0) return result;
            start++;
            while (start < json.Length)
            {
                while (start < json.Length && (char.IsWhiteSpace(json[start]) || json[start] == ',')) start++;
                if (start >= json.Length || json[start] == ']') break;
                if (json[start] != '"') break;
                start++;
                var sb = new StringBuilder();
                for (; start < json.Length; start++)
                {
                    var c = json[start];
                    if (c == '\\' && start + 1 < json.Length)
                    {
                        sb.Append(json[start + 1]);
                        start++;
                        continue;
                    }
                    if (c == '"') { start++; break; }
                    sb.Append(c);
                }
                var v = sb.ToString().Trim();
                if (v.Length > 0) result.Add(v);
            }
            return result;
        }
    }
}
#endif
