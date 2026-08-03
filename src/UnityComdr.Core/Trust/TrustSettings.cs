using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityComdr.Trust;

/// <summary>
/// FR-T2 local trust settings: per-tool / per-skill disable lists (no phone-home).
/// File shape matches <c>ProjectSettings/UnityComdr.mcp.json</c>.
/// </summary>
public sealed class TrustSettings
{
    public const string DefaultRelativePath = "ProjectSettings/UnityComdr.mcp.json";
    public const string DisabledToolsEnv = "UNITY_COMDR_DISABLED_TOOLS";
    public const string DisabledSkillsEnv = "UNITY_COMDR_DISABLED_SKILLS";
    public const string TrustSettingsPathEnv = "UNITY_COMDR_TRUST_SETTINGS";
    public const string AuditLogPathEnv = "UNITY_COMDR_AUDIT_LOG";

    private readonly HashSet<string> _disabledTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabledSkills = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When true, hosts may register a local file audit sink (FR-T3).</summary>
    public bool AuditEnabled { get; set; } = true;

    [JsonIgnore]
    public IReadOnlyCollection<string> DisabledTools => _disabledTools;

    [JsonIgnore]
    public IReadOnlyCollection<string> DisabledSkills => _disabledSkills;

    public bool IsToolDisabled(string? name) =>
        !string.IsNullOrEmpty(name) && _disabledTools.Contains(name);

    public bool IsSkillDisabled(string? skillId) =>
        !string.IsNullOrEmpty(skillId) && _disabledSkills.Contains(skillId);

    public void SetDisabledTools(IEnumerable<string>? names)
    {
        _disabledTools.Clear();
        if (names == null) return;
        foreach (var n in names)
        {
            if (!string.IsNullOrWhiteSpace(n))
                _disabledTools.Add(n.Trim());
        }
    }

    public void SetDisabledSkills(IEnumerable<string>? ids)
    {
        _disabledSkills.Clear();
        if (ids == null) return;
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id))
                _disabledSkills.Add(id.Trim());
        }
    }

    public void DisableTool(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            _disabledTools.Add(name.Trim());
    }

    public void DisableSkill(string skillId)
    {
        if (!string.IsNullOrWhiteSpace(skillId))
            _disabledSkills.Add(skillId.Trim());
    }

    public string ToJson()
    {
        var dto = new TrustSettingsDto
        {
            DisabledTools = _disabledTools.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            DisabledSkills = _disabledSkills.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            AuditEnabled = AuditEnabled
        };
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static TrustSettings FromJson(string json)
    {
        var settings = new TrustSettings();
        if (string.IsNullOrWhiteSpace(json)) return settings;
        var dto = JsonSerializer.Deserialize<TrustSettingsDto>(json, JsonOptions);
        if (dto == null) return settings;
        settings.SetDisabledTools(dto.DisabledTools);
        settings.SetDisabledSkills(dto.DisabledSkills);
        settings.AuditEnabled = dto.AuditEnabled;
        return settings;
    }

    /// <summary>
    /// Headless/InMemory: load from <see cref="TrustSettingsPathEnv"/>, cwd <see cref="DefaultRelativePath"/>,
    /// and/or comma-separated <see cref="DisabledToolsEnv"/> / <see cref="DisabledSkillsEnv"/>.
    /// Returns null when nothing is configured.
    /// </summary>
    public static TrustSettings? TryLoadFromEnvironment(string? projectRoot = null)
    {
        TrustSettings? settings = null;
        var root = projectRoot ?? Environment.CurrentDirectory;

        var pathEnv = Environment.GetEnvironmentVariable(TrustSettingsPathEnv);
        var path = !string.IsNullOrWhiteSpace(pathEnv)
            ? pathEnv
            : Path.Combine(root, DefaultRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(path))
        {
            try
            {
                settings = FromJson(File.ReadAllText(path));
            }
            catch
            {
                settings = new TrustSettings();
            }
        }

        var toolsEnv = Environment.GetEnvironmentVariable(DisabledToolsEnv);
        var skillsEnv = Environment.GetEnvironmentVariable(DisabledSkillsEnv);
        if (!string.IsNullOrWhiteSpace(toolsEnv) || !string.IsNullOrWhiteSpace(skillsEnv))
        {
            settings ??= new TrustSettings();
            if (!string.IsNullOrWhiteSpace(toolsEnv))
            {
                foreach (var t in toolsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    settings.DisableTool(t);
            }
            if (!string.IsNullOrWhiteSpace(skillsEnv))
            {
                foreach (var s in skillsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    settings.DisableSkill(s);
            }
        }

        return settings;
    }

    /// <summary>
    /// Resolve append-only audit log path: env <see cref="AuditLogPathEnv"/>, else
    /// <c>{projectRoot}/Temp/unity-comdr-audit.jsonl</c> (fallback <c>Logs/</c>).
    /// </summary>
    public static string ResolveAuditLogPath(string? projectRoot = null)
    {
        var env = Environment.GetEnvironmentVariable(AuditLogPathEnv);
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        var root = projectRoot ?? Environment.CurrentDirectory;
        var tempDir = Path.Combine(root, "Temp");
        if (Directory.Exists(tempDir) || Directory.Exists(Path.Combine(root, "Assets")))
            return Path.Combine(tempDir, "unity-comdr-audit.jsonl");
        return Path.Combine(root, "Logs", "unity-comdr-audit.jsonl");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private sealed class TrustSettingsDto
    {
        public List<string>? DisabledTools { get; set; }
        public List<string>? DisabledSkills { get; set; }
        public bool AuditEnabled { get; set; } = true;
    }
}
