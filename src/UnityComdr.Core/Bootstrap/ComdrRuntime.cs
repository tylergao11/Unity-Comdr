using UnityComdr.Editor;
using UnityComdr.Mcp;
using UnityComdr.Skills;
using UnityComdr.Tools;
using UnityComdr.Trust;

namespace UnityComdr.Bootstrap;

/// <summary>
/// Single composition root: wires editor host → registry → core tools → domain skills → escape hatches → resources/prompts.
/// </summary>
public sealed class ComdrRuntime
{
    public IEditorHost Editor { get; }
    public ToolRegistry Registry { get; }
    public ResourceCatalog Resources { get; }
    public PromptCatalog Prompts { get; }

    /// <param name="editor">Editor host (live bridge or InMemory).</param>
    /// <param name="trust">
    /// Optional FR-T2 settings. When null, loads from env/file if present
    /// (<c>UNITY_COMDR_TRUST_SETTINGS</c>, <c>ProjectSettings/UnityComdr.mcp.json</c>,
    /// <c>UNITY_COMDR_DISABLED_TOOLS</c> / <c>UNITY_COMDR_DISABLED_SKILLS</c>).
    /// File audit sink is registered only when <c>UNITY_COMDR_AUDIT_LOG</c> is set (tests inject sinks via API).
    /// </param>
    public ComdrRuntime(IEditorHost? editor = null, TrustSettings? trust = null)
    {
        Editor = editor ?? new InMemoryEditorHost();
        Registry = new ToolRegistry();
        Registry.BindEditor(Editor);

        var settings = trust ?? TrustSettings.TryLoadFromEnvironment();
        if (settings != null)
            Registry.ApplyTrustSettings(settings);

        // Opt-in file audit for headless via env path; live Editor bridge enables audit by default separately.
        var auditPath = Environment.GetEnvironmentVariable(TrustSettings.AuditLogPathEnv);
        if (!string.IsNullOrWhiteSpace(auditPath) && (settings?.AuditEnabled ?? true))
            Registry.SetAuditSink(new FileAuditSink(auditPath));

        CoreTools.RegisterAll(Registry, Editor);
        DomainSkills.RegisterAll(Registry, Editor);
        CoreTools.RegisterEscapeHatches(Registry, Editor);
        Resources = new ResourceCatalog(Editor, Registry);
        Prompts = new PromptCatalog();
    }

    /// <summary>Default active tool names for assertions / MCP tools/list.</summary>
    public IReadOnlyList<string> DefaultToolNames =>
        Registry.GetActiveTools().Select(t => t.Name).ToList();
}
