using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.Editor;
using UnityComdr.McpHost;
using UnityComdr.Skills;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// Competitive-parity coverage: domain skills, resources, prompts, expanded core actions.
/// </summary>
public class ParityAndDomainSkillTests
{
    [Fact]
    public void Domain_skill_catalog_covers_popular_mcp_areas()
    {
        var rt = new ComdrRuntime();
        var ids = rt.Registry.ListSkills().Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var meta in DomainSkills.CatalogMeta())
            Assert.Contains(meta.Id, ids);

        // Default still budgeted
        Assert.True(rt.Registry.ActiveToolCount <= Tools.ToolBudget.MaxDefaultCoreTools);
        Assert.Equal(DomainSkills.CatalogMeta().Count, rt.Registry.ListSkills().Count);
    }

    [Fact]
    public async Task Playmode_packages_menu_selection_profiling_screenshots_batch()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        await Load(rt, DomainSkills.PlayModeId);
        var play = await rt.Registry.CallAsync("playmode_control", Obj(("action", "play")));
        Assert.False(play.IsError);
        Assert.Contains("\"isPlaying\":true", play.Content.Replace(" ", ""));

        await Load(rt, DomainSkills.PackagesId);
        var add = await rt.Registry.CallAsync("package_manage", Obj(
            ("action", "add"), ("package", "com.unity.cinemachine@2.9.0")));
        Assert.True(add.IsError, "headless package add must isError (requires live Client API)");
        Assert.Contains("requires_live", add.Content, StringComparison.OrdinalIgnoreCase);

        await Load(rt, DomainSkills.MenuId);
        var menu = await rt.Registry.CallAsync("menu_manage", Obj(
            ("action", "execute"), ("path", "GameObject/3D Object/Cube")));
        Assert.False(menu.IsError);
        var cube = await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "get"), ("target", "Cube")));
        Assert.False(cube.IsError);

        await Load(rt, DomainSkills.SelectionId);
        var sel = await rt.Registry.CallAsync("selection_manage", Obj(
            ("action", "set"), ("goIds", host.FindGameObject("Cube")!.Id)));
        Assert.False(sel.IsError);
        Assert.Contains(host.FindGameObject("Cube")!.Id, sel.Content);

        await Load(rt, DomainSkills.ProfilingId);
        var prof = await rt.Registry.CallAsync("profiler_manage", Obj(("action", "start")));
        Assert.False(prof.IsError);
        Assert.Contains("\"enabled\":true", prof.Content.Replace(" ", ""));

        await Load(rt, DomainSkills.ScreenshotsId);
        var shot = await rt.Registry.CallAsync("screenshot_capture", Obj(("source", "game_view")));
        Assert.True(shot.IsError);
        Assert.DoesNotContain("payloadMarker", shot.Content);
        Assert.True(
            shot.Content.Contains("real pixels", StringComparison.OrdinalIgnoreCase) ||
            shot.Content.Contains("no_live_pixels", StringComparison.OrdinalIgnoreCase) ||
            shot.Content.Contains("no live Editor", StringComparison.OrdinalIgnoreCase),
            shot.Content);

        await Load(rt, DomainSkills.BatchId);
        var batch = await rt.Registry.CallAsync("batch_execute", Obj(
            ("callsJson",
                """[{"name":"console_clear","arguments":{}},{"name":"editor_state","arguments":{}}]""")));
        Assert.False(batch.IsError);
        Assert.Contains("console_clear", batch.Content);
        Assert.Contains("editor_state", batch.Content);
    }

    [Fact]
    public async Task Expanded_core_scene_go_assets_parity_actions()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        await rt.Registry.CallAsync("scene_manage", Obj(
            ("action", "create"), ("path", "Assets/Scenes/A.unity"), ("name", "A")));
        await rt.Registry.CallAsync("scene_manage", Obj(
            ("action", "create"), ("path", "Assets/Scenes/B.unity"), ("name", "B")));
        await rt.Registry.CallAsync("scene_manage", Obj(
            ("action", "open"), ("path", "Assets/Scenes/A.unity"), ("additive", true)));
        var opened = await rt.Registry.CallAsync("scene_manage", Obj(("action", "list_opened")));
        Assert.Contains("A.unity", opened.Content);

        var go = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "create"), ("name", "Hero"), ("primitive", "Capsule"), ("tag", "Player")));
        Assert.False(go.IsError);
        Assert.Contains("Player", go.Content);

        var find = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "find"), ("tag", "Player")));
        Assert.Contains("Hero", find.Content);

        await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "create_folder"), ("path", "Assets/Art/Textures")));
        await rt.Registry.CallAsync("script_write", Obj(
            ("path", "Assets/Scripts/Foo.cs"), ("content", "class Foo {}")));
        var copy = await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "copy"),
            ("fromPath", "Assets/Scripts/Foo.cs"),
            ("toPath", "Assets/Scripts/FooCopy.cs")));
        Assert.False(copy.IsError);
        var shaders = await rt.Registry.CallAsync("assets_manage", Obj(("action", "list_shaders")));
        Assert.Contains("Standard", shaders.Content);

        var types = await rt.Registry.CallAsync("component_manage", Obj(("action", "list_types"), ("filter", "Rigid")));
        Assert.Contains("Rigidbody", types.Content);
    }

    [Fact]
    public async Task Mcp_resources_and_prompts_protocol()
    {
        var server = new McpServer(new ComdrRuntime(), new StringReader(""), new StringWriter());
        var resList = await server.HandleLineAsync("""{"jsonrpc":"2.0","id":1,"method":"resources/list","params":{}}""");
        Assert.NotNull(resList?["result"]?["resources"]);
        var uris = (resList!["result"]!["resources"] as JsonArray)!
            .Select(n => n?["uri"]?.GetValue<string>()).ToHashSet();
        Assert.Contains("unity://hierarchy", uris);
        Assert.Contains("unity://console", uris);
        Assert.Contains("unity://skills", uris);

        var resRead = await server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"resources/read","params":{"uri":"unity://editor-state"}}""");
        var text = resRead!["result"]?["contents"]?[0]?["text"]?.GetValue<string>();
        Assert.Contains("isCompiling", text);

        var prompts = await server.HandleLineAsync("""{"jsonrpc":"2.0","id":3,"method":"prompts/list","params":{}}""");
        Assert.Contains("code_fix_loop", prompts!.ToJsonString());

        var prompt = await server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":4,"method":"prompts/get","params":{"name":"code_fix_loop"}}""");
        Assert.Contains("console_read", prompt!.ToJsonString());
    }

    private static async Task Load(ComdrRuntime rt, string id)
    {
        var r = await rt.Registry.CallAsync("skill_manage", Obj(("action", "load"), ("id", id)));
        Assert.False(r.IsError, r.Content);
    }

    private static JsonObject Obj(params (string k, object v)[] pairs)
    {
        var o = new JsonObject();
        foreach (var (k, v) in pairs)
        {
            o[k] = v switch
            {
                string s => s,
                bool b => b,
                int i => i,
                JsonObject jo => jo,
                _ => v.ToString()
            };
        }
        return o;
    }
}
