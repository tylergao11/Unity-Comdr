using System.Text.Json;
using UnityComdr.Editor;
using UnityComdr.Skills;
using UnityComdr.Tools;
using UnityComdr.Util;

namespace UnityComdr.Mcp;

/// <summary>
/// MCP Resources inspired by CoderGamester unity:// hierarchy, logs, packages, assets, tests.
/// Token-frugal: resources return compact summaries by default.
/// </summary>
public sealed class ResourceCatalog
{
    private readonly IEditorHost _editor;
    private readonly ToolRegistry _registry;

    public ResourceCatalog(IEditorHost editor, ToolRegistry registry)
    {
        _editor = editor;
        _registry = registry;
    }

    public IReadOnlyList<ResourceDescriptor> List() => new[]
    {
        new ResourceDescriptor("unity://console", "Unity Console", "Recent console logs (compact)"),
        new ResourceDescriptor("unity://hierarchy", "Scene Hierarchy", "Active scene hierarchy summary"),
        new ResourceDescriptor("unity://scene", "Active Scene", "Active scene metadata"),
        new ResourceDescriptor("unity://editor-state", "Editor State", "Compile / play mode / active scene"),
        new ResourceDescriptor("unity://packages", "Packages", "Installed UPM packages"),
        new ResourceDescriptor("unity://assets", "Assets Index", "Asset listing (paginated)"),
        new ResourceDescriptor("unity://skills", "Skills Catalog", "Available domain skills and load state"),
        new ResourceDescriptor("unity://selection", "Selection", "Current Editor selection"),
        new ResourceDescriptor("unity://menu-items", "Menu Items", "Known menu item paths")
    };

    public string Read(string uri)
    {
        uri = uri.Trim();
        object payload;
        switch (uri.ToLowerInvariant())
        {
            case "unity://console":
                payload = CompactResults.Paginate(
                    _editor.GetConsoleLogs(), 0, 30,
                    l => new { type = l.Type.ToString(), l.Message, l.File, l.Line });
                break;
            case "unity://hierarchy":
                payload = CompactResults.HierarchySummary(
                    _editor.GetAllGameObjects(), _editor.GetActiveScene().RootObjectIds);
                break;
            case "unity://scene":
                payload = _editor.GetActiveScene();
                break;
            case "unity://editor-state":
                payload = _editor.GetState();
                break;
            case "unity://packages":
                payload = new { packages = _editor.ListPackages() };
                break;
            case "unity://assets":
                payload = CompactResults.Paginate(
                    _editor.FindAssets(), 0, 40, a => new { a.Path, a.Kind });
                break;
            case "unity://skills":
                payload = new
                {
                    skills = _registry.ListSkills().Select(s => new
                    {
                        s.Id,
                        s.Name,
                        s.Description,
                        toolCount = s.Tools.Count,
                        loaded = _registry.LoadedSkillIds.Contains(s.Id)
                    }),
                    meta = DomainSkills.CatalogMeta()
                };
                break;
            case "unity://selection":
                payload = _editor.GetSelection();
                break;
            case "unity://menu-items":
                payload = new { items = _editor.ListMenuItems() };
                break;
            default:
                throw new InvalidOperationException($"Unknown resource: {uri}");
        }
        return JsonSerializer.Serialize(payload, CompactResults.JsonOptions);
    }
}

public sealed record ResourceDescriptor(string Uri, string Name, string Description);
