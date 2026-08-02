using System.Text.Json.Nodes;

namespace UnityComdr.Tools;

/// <summary>
/// Session tool catalog: always-on core + explicitly loaded skills + gated escape hatches.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _core = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SkillDefinition> _skills = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadedSkills = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolDefinition> _escapeHatches = new(StringComparer.OrdinalIgnoreCase);
    private bool _escapeHatchesEnabled;

    public bool EscapeHatchesEnabled
    {
        get => _escapeHatchesEnabled;
        set => _escapeHatchesEnabled = value;
    }

    public IReadOnlyCollection<string> LoadedSkillIds => _loadedSkills.ToList();

    public void RegisterCore(ToolDefinition tool)
    {
        if (tool.SkillId != null)
            throw new ArgumentException("Core tools must not set SkillId", nameof(tool));
        if (tool.IsEscapeHatch)
            throw new ArgumentException("Use RegisterEscapeHatch for escape hatches", nameof(tool));
        _core[tool.Name] = tool;
    }

    public void RegisterSkill(SkillDefinition skill)
    {
        _skills[skill.Id] = skill;
    }

    public void RegisterEscapeHatch(ToolDefinition tool)
    {
        tool = new ToolDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = tool.InputSchema,
            Handler = tool.Handler,
            SkillId = tool.SkillId,
            IsEscapeHatch = true,
            EnabledByDefault = false
        };
        _escapeHatches[tool.Name] = tool;
    }

    public bool LoadSkill(string skillId)
    {
        if (!_skills.ContainsKey(skillId))
            return false;
        _loadedSkills.Add(skillId);
        return true;
    }

    public bool UnloadSkill(string skillId) => _loadedSkills.Remove(skillId);

    public void UnloadAllSkills() => _loadedSkills.Clear();

    public IReadOnlyList<SkillDefinition> ListSkills() =>
        _skills.Values.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToList();

    public SkillDefinition? GetSkill(string skillId) =>
        _skills.TryGetValue(skillId, out var s) ? s : null;

    /// <summary>Tools visible to MCP tools/list for this session.</summary>
    public IReadOnlyList<ToolDefinition> GetActiveTools()
    {
        var list = new List<ToolDefinition>();
        list.AddRange(_core.Values.Where(t => t.EnabledByDefault || true));

        foreach (var skillId in _loadedSkills)
        {
            if (_skills.TryGetValue(skillId, out var skill))
                list.AddRange(skill.Tools);
        }

        if (_escapeHatchesEnabled)
            list.AddRange(_escapeHatches.Values);

        return list.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public int ActiveToolCount => GetActiveTools().Count;

    public int CoreToolCount => _core.Count;

    public ToolDefinition? FindActive(string name) =>
        GetActiveTools().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public async Task<ToolResult> CallAsync(string name, JsonObject? args, CancellationToken ct = default)
    {
        var tool = FindActive(name);
        if (tool == null)
        {
            // Helpful error if skill not loaded
            foreach (var skill in _skills.Values)
            {
                if (skill.Tools.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    return ToolResult.Error(
                        $"Tool '{name}' belongs to skill '{skill.Id}' which is not loaded. Call skill_manage action=load id='{skill.Id}' first.");
                }
            }
            if (_escapeHatches.ContainsKey(name) && !_escapeHatchesEnabled)
                return ToolResult.Error($"Escape hatch '{name}' is disabled. Enable with escape_hatches_set enabled=true.");
            return ToolResult.Error($"Unknown or inactive tool: {name}");
        }
        return await tool.Handler(args, ct).ConfigureAwait(false);
    }
}
