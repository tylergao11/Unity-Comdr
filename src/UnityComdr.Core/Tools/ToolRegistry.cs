using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityComdr.Editor;
using UnityComdr.Trust;
using UnityComdr.Util;

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
    private IEditorHost? _editor;
    private TrustSettings _trust = new();
    private IAuditSink? _audit;

    /// <summary>Bind host so CallAsync can refuse work during Editor busy transitions (FR-R1).</summary>
    public void BindEditor(IEditorHost editor) => _editor = editor;

    /// <summary>FR-T2 local disable lists (injectable for tests; also loaded from env/file).</summary>
    public TrustSettings Trust => _trust;

    /// <summary>Apply FR-T2 disable list / audit flag. Replaces prior settings.</summary>
    public void ApplyTrustSettings(TrustSettings? settings) =>
        _trust = settings ?? new TrustSettings();

    /// <summary>Inject disabled tool names (FR-T2 / tests).</summary>
    public void SetDisabledTools(IEnumerable<string> names)
    {
        _trust.SetDisabledTools(names);
    }

    /// <summary>Inject disabled skill ids (FR-T2 / tests).</summary>
    public void SetDisabledSkills(IEnumerable<string> ids)
    {
        _trust.SetDisabledSkills(ids);
    }

    /// <summary>Register FR-T3 audit sink; when set, every CallAsync appends a local record.</summary>
    public void SetAuditSink(IAuditSink? sink) => _audit = sink;

    public IAuditSink? AuditSink => _audit;

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
        if (_trust.IsSkillDisabled(skillId))
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

    /// <summary>Tools visible to MCP tools/list for this session (FR-T2 disable list applied).</summary>
    public IReadOnlyList<ToolDefinition> GetActiveTools()
    {
        var list = new List<ToolDefinition>();
        list.AddRange(_core.Values.Where(t => t.EnabledByDefault || true));

        foreach (var skillId in _loadedSkills)
        {
            if (_trust.IsSkillDisabled(skillId))
                continue;
            if (_skills.TryGetValue(skillId, out var skill))
                list.AddRange(skill.Tools);
        }

        if (_escapeHatchesEnabled)
            list.AddRange(_escapeHatches.Values);

        return list
            .Where(t => !_trust.IsToolDisabled(t.Name)
                        && (t.SkillId == null || !_trust.IsSkillDisabled(t.SkillId)))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public int ActiveToolCount => GetActiveTools().Count;

    public int CoreToolCount => _core.Count;

    public ToolDefinition? FindActive(string name) =>
        GetActiveTools().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public async Task<ToolResult> CallAsync(string name, JsonObject? args, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        ToolResult result;
        try
        {
            result = await CallCoreAsync(name, args, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordAudit(name, ok: false, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }

        sw.Stop();
        RecordAudit(name, ok: !result.IsError, sw.ElapsedMilliseconds, result.IsError ? Truncate(result.Content, 240) : null);
        return result;
    }

    private async Task<ToolResult> CallCoreAsync(string name, JsonObject? args, CancellationToken ct)
    {
        if (_trust.IsToolDisabled(name))
        {
            return ToolResult.ErrorEnvelope(
                "tool_disabled",
                $"Tool '{name}' is disabled by local Trust settings (ProjectSettings/UnityComdr.mcp.json).",
                suggestion: "Remove the tool from disabledTools in UnityComdr.mcp.json or the Editor Trust panel.",
                nextStep: "Update the local disable list, then call tools/list and retry.");
        }

        var tool = FindActive(name);
        if (tool == null)
        {
            // Helpful error if skill not loaded / disabled
            foreach (var skill in _skills.Values)
            {
                if (skill.Tools.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (_trust.IsSkillDisabled(skill.Id))
                    {
                        return ToolResult.ErrorEnvelope(
                            "skill_disabled",
                            $"Tool '{name}' belongs to skill '{skill.Id}' which is disabled by local Trust settings.",
                            nextStep: "Remove the skill id from disabledSkills, then load the skill and retry.");
                    }

                    var msg =
                        $"Tool '{name}' belongs to skill '{skill.Id}' which is not loaded. Call skill_manage action=load id='{skill.Id}' first.";
                    return ToolResult.ErrorEnvelope(
                        "skill_not_loaded",
                        msg,
                        suggestion: $"Load skill '{skill.Id}' before calling '{name}'.",
                        nextStep: $"Call skill_manage action=load id='{skill.Id}' then retry '{name}'.");
                }
            }
            if (_escapeHatches.ContainsKey(name) && !_escapeHatchesEnabled)
            {
                return ToolResult.ErrorEnvelope(
                    "escape_hatch_disabled",
                    $"Escape hatch '{name}' is disabled. Enable with escape_hatches_set enabled=true.",
                    nextStep: "Call escape_hatches_set with enabled=true, then retry.");
            }
            return ToolResult.ErrorEnvelope(
                "unknown_tool",
                $"Unknown or inactive tool: {name}",
                suggestion: "Use skill_manage action=list to see loadable skills.",
                nextStep: "Call skill_manage action=list, load the owning skill, then retry.");
        }

        // FR-R1 / PR-5: immediate busy — do not hang or fake success during transitions.
        // editor_state remains available so agents can poll the lifecycle phase.
        if (_editor != null && !IsLifecycleProbe(name))
        {
            try
            {
                var state = _editor.GetState();
                if (EditorLifecyclePhases.IsBusy(state.Phase))
                {
                    var msg = EditorLifecyclePhases.FormatBusyMessage(
                        state.Phase,
                        state.SuggestedRetrySeconds);
                    return ToolResult.ErrorEnvelope(
                        state.Phase,
                        msg,
                        nextStep: EditorLifecyclePhases.DefaultNextStep(state.Phase));
                }
            }
            catch (EditorBusyException busy)
            {
                return ToolResult.ErrorEnvelope(busy.Phase, busy.Message, nextStep: busy.NextStep);
            }
        }

        try
        {
            var raw = await tool.Handler(args, ct).ConfigureAwait(false);
            return EnsureEnvelope(raw);
        }
        catch (EditorBusyException busy)
        {
            return ToolResult.ErrorEnvelope(busy.Phase, busy.Message, nextStep: busy.NextStep);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("stale_reference", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("sessionGeneration", StringComparison.OrdinalIgnoreCase))
        {
            // O2 / FR-A5: domain-reload invalidates instance ids — surface actionable error, never silent wrong-object ops.
            return ToolResult.ErrorEnvelope(
                "stale_reference",
                ex.Message,
                suggestion: "Instance ids are invalid after domain reload.",
                nextStep: "Re-query by hierarchy path (gameobject_manage action=find / hierarchy_get), then retry with a fresh id.");
        }
        catch (ArgumentException ex)
        {
            return ToolResult.ErrorEnvelope(
                "bad_argument",
                ex.Message,
                nextStep: "Fix the missing/invalid argument and retry.");
        }
        catch (Exception ex)
        {
            return ToolResult.ErrorEnvelope(
                "tool_exception",
                ex.Message,
                nextStep: "Inspect editor_state / console_read, then retry or adjust inputs.");
        }
    }

    private void RecordAudit(string toolName, bool ok, long durationMs, string? error)
    {
        if (_audit == null) return;
        try
        {
            _audit.Append(new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                ToolName = toolName,
                Ok = ok,
                DurationMs = durationMs,
                Error = error
            });
        }
        catch
        {
            // Audit must never break tool calls.
        }
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
        return text.Substring(0, max) + "…";
    }

    /// <summary>O3: wrap every tool JSON success/error into a consistent envelope (MCP text stays parseable).</summary>
    internal static ToolResult EnsureEnvelope(ToolResult result)
    {
        if (result.IsEnvelope)
            return result;

        if (result.IsError)
        {
            if (EditorBusyException.TryParse(result.Content, out var busy) && busy != null)
            {
                return ToolResult.ErrorEnvelope(busy.Phase, result.Content, nextStep: busy.NextStep);
            }

            if (result.Content.Contains("stale_reference", StringComparison.OrdinalIgnoreCase))
            {
                return ToolResult.ErrorEnvelope(
                    "stale_reference",
                    result.Content,
                    suggestion: "Instance ids are invalid after domain reload.",
                    nextStep: "Re-query by hierarchy path, then retry with a fresh id.");
            }

            var code = result.Content.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? "not_found"
                : "tool_error";
            return ToolResult.ErrorEnvelope(code, result.Content);
        }

        object data;
        if (result.Structured != null)
        {
            var structuredJson = JsonSerializer.Serialize(result.Structured, CompactResults.JsonOptions);
            if (JsonTextEquivalent(result.Content, structuredJson))
                data = result.Structured;
            else
                // Ok(text, structured) — keep text parseable for agents/tests (e.g. script_read).
                data = new { text = result.Content, details = result.Structured };
        }
        else if (TryParseJson(result.Content, out var node) && node != null)
            data = node;
        else
            data = result.Content;

        var enveloped = ToolResult.FromEnvelope(ok: true, data: data, hint: null, images: result.Images);
        return enveloped;
    }

    private static bool JsonTextEquivalent(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            var na = JsonNode.Parse(a);
            var nb = JsonNode.Parse(b);
            return na?.ToJsonString() == nb?.ToJsonString();
        }
        catch
        {
            return string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);
        }
    }

    private static bool TryParseJson(string? text, out JsonNode? node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.TrimStart();
        if (t.Length == 0 || (t[0] != '{' && t[0] != '[')) return false;
        try
        {
            node = JsonNode.Parse(text);
            return node != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLifecycleProbe(string name) =>
        name.Equals("editor_state", StringComparison.OrdinalIgnoreCase);
}
