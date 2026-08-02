using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.Editor;
using UnityComdr.Skills;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// Production-grade: every domain skill must yield real, non-empty, behaviorally correct
/// outcomes via shipped registry CallAsync on the shared host path.
/// </summary>
public class SkillSurfaceProductionTests
{
    [Fact]
    public async Task Every_registered_domain_skill_has_successful_representative_call()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);
        var skillIds = rt.Registry.ListSkills().Select(s => s.Id).OrderBy(x => x).ToList();
        Assert.Equal(DomainSkills.CatalogMeta().Count, skillIds.Count);

        foreach (var id in skillIds)
        {
            var load = await rt.Registry.CallAsync("skill_manage", Obj(("action", "load"), ("id", id)));
            Assert.False(load.IsError, $"load {id}: {load.Content}");
        }

        // testing
        var tests = await rt.Registry.CallAsync("tests_run", Obj(("mode", "EditMode")));
        Assert.False(tests.IsError);
        Assert.Contains("results", tests.Content);
        var testsList = await rt.Registry.CallAsync("tests_list", null);
        Assert.False(testsList.IsError);
        Assert.Contains("Console_NoErrors", testsList.Content);

        // prefab-advanced (need a prefab first)
        await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "create"), ("name", "BatchSrc"), ("primitive", "Cube")));
        await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "prefab_create"), ("path", "Assets/Prefabs/Batch.prefab"), ("target", "BatchSrc")));
        var batchInst = await rt.Registry.CallAsync("prefab_batch_instantiate", Obj(
            ("path", "Assets/Prefabs/Batch.prefab"), ("count", 3), ("namePrefix", "B")));
        Assert.False(batchInst.IsError, batchInst.Content);
        Assert.Contains("\"count\":3", batchInst.Content.Replace(" ", ""));
        var prefabList = await rt.Registry.CallAsync("prefab_list", null);
        Assert.False(prefabList.IsError);
        Assert.Contains("Batch.prefab", prefabList.Content);

        // playmode
        Assert.False((await rt.Registry.CallAsync("playmode_control", Obj(("action", "play")))).IsError);
        Assert.Contains("\"isPlaying\":true", (await rt.Registry.CallAsync("editor_state", null)).Content.Replace(" ", ""));
        Assert.False((await rt.Registry.CallAsync("playmode_control", Obj(("action", "pause")))).IsError);
        Assert.False((await rt.Registry.CallAsync("playmode_control", Obj(("action", "step")))).IsError);
        Assert.False((await rt.Registry.CallAsync("playmode_control", Obj(("action", "stop")))).IsError);

        // selection
        var cube = host.FindGameObject("BatchSrc") ?? host.FindGameObject("B_1");
        Assert.NotNull(cube);
        var selSet = await rt.Registry.CallAsync("selection_manage", Obj(("action", "set"), ("goIds", cube!.Id)));
        Assert.False(selSet.IsError, selSet.Content);
        Assert.Contains(cube.Id, selSet.Content);
        var selGet = await rt.Registry.CallAsync("selection_manage", Obj(("action", "get")));
        Assert.Contains(cube.Id, selGet.Content);

        // packages — list non-empty, search catalog, add
        var pkgList = await rt.Registry.CallAsync("package_manage", Obj(("action", "list")));
        Assert.False(pkgList.IsError);
        Assert.Contains("com.unity", pkgList.Content);
        Assert.DoesNotContain("\"packages\":[]", pkgList.Content.Replace(" ", ""));
        var pkgSearch = await rt.Registry.CallAsync("package_manage", Obj(("action", "search"), ("query", "cinemachine")));
        Assert.False(pkgSearch.IsError);
        Assert.Contains("cinemachine", pkgSearch.Content, StringComparison.OrdinalIgnoreCase);
        var pkgAdd = await rt.Registry.CallAsync("package_manage", Obj(("action", "add"), ("package", "com.unity.cinemachine@2.9.7")));
        Assert.False(pkgAdd.IsError);
        Assert.Contains("cinemachine", pkgAdd.Content, StringComparison.OrdinalIgnoreCase);

        // menu — list non-empty + execute side effect
        var menuList = await rt.Registry.CallAsync("menu_manage", Obj(("action", "list")));
        Assert.False(menuList.IsError);
        Assert.Contains("GameObject/Create Empty", menuList.Content);
        var menuExec = await rt.Registry.CallAsync("menu_manage", Obj(
            ("action", "execute"), ("path", "GameObject/3D Object/Sphere")));
        Assert.False(menuExec.IsError);
        var sphere = await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "get"), ("target", "Sphere")));
        Assert.False(sphere.IsError);

        // profiling — start yields enabled + non-zero metrics; save/load
        var profStart = await rt.Registry.CallAsync("profiler_manage", Obj(("action", "start")));
        Assert.False(profStart.IsError);
        Assert.Contains("\"enabled\":true", profStart.Content.Replace(" ", ""));
        Assert.Contains("monoUsedBytes", profStart.Content);
        var profCap = await rt.Registry.CallAsync("profiler_manage", Obj(("action", "capture")));
        Assert.False(profCap.IsError);
        var profSave = await rt.Registry.CallAsync("profiler_manage", Obj(
            ("action", "save"), ("path", "Assets/Profiler/snap.json")));
        Assert.False(profSave.IsError);
        var profLoad = await rt.Registry.CallAsync("profiler_manage", Obj(
            ("action", "load"), ("path", "Assets/Profiler/snap.json")));
        Assert.False(profLoad.IsError);
        Assert.Contains("enabled", profLoad.Content);

        // screenshots — non-empty payloadMarker
        var shot = await rt.Registry.CallAsync("screenshot_capture", Obj(("source", "game_view")));
        Assert.False(shot.IsError);
        Assert.Contains("payloadMarker", shot.Content);
        Assert.True(shot.Content.Length > 40);

        // batch
        var batch = await rt.Registry.CallAsync("batch_execute", Obj(
            ("callsJson",
                """[{"name":"console_clear","arguments":{}},{"name":"editor_state","arguments":{}}]""")));
        Assert.False(batch.IsError);
        Assert.Contains("console_clear", batch.Content);
        Assert.Contains("editor_state", batch.Content);

        // still can unload all and stay at core budget
        foreach (var id in skillIds)
            await rt.Registry.CallAsync("skill_manage", Obj(("action", "unload"), ("id", id)));
        Assert.True(rt.Registry.ActiveToolCount <= Tools.ToolBudget.MaxDefaultCoreTools);
    }

    [Fact]
    public async Task Core_assets_and_console_paths_non_empty_on_shared_host()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);
        host.AddConsoleLog(new Models.ConsoleLogEntry(Models.LogType.Error, "boom-error-xyz"));
        var err = await rt.Registry.CallAsync("console_read", Obj(("type", "Error")));
        Assert.Contains("boom-error-xyz", err.Content);

        await rt.Registry.CallAsync("assets_manage", Obj(("action", "create_folder"), ("path", "Assets/Art")));
        var find = await rt.Registry.CallAsync("assets_manage", Obj(("action", "find"), ("kind", "Folder")));
        Assert.Contains("Assets", find.Content);
        var shaders = await rt.Registry.CallAsync("assets_manage", Obj(("action", "list_shaders")));
        Assert.Contains("Standard", shaders.Content);
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
