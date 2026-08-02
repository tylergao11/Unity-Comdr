using UnityComdr.Editor;
using UnityComdr.Mcp;
using UnityComdr.Skills;
using UnityComdr.Tools;

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

    public ComdrRuntime(IEditorHost? editor = null)
    {
        Editor = editor ?? new InMemoryEditorHost();
        Registry = new ToolRegistry();
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
