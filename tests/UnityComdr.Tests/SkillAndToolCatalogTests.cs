using UnityComdr.Bootstrap;
using UnityComdr.Skills;
using UnityComdr.Tools;
using Xunit;

namespace UnityComdr.Tests;

public class SkillAndToolCatalogTests
{
    [Fact]
    public void Default_session_core_tool_count_within_budget()
    {
        var rt = new ComdrRuntime();
        var active = rt.Registry.GetActiveTools();

        Assert.True(active.Count <= ToolBudget.MaxDefaultCoreTools,
            $"Default tools={active.Count} exceeds budget {ToolBudget.MaxDefaultCoreTools}: {string.Join(", ", active.Select(t => t.Name))}");
        Assert.Equal(rt.Registry.CoreToolCount, active.Count);
        Assert.DoesNotContain(active, t => t.Name == "tests_run");
        Assert.DoesNotContain(active, t => t.Name == "reflect_call");
        Assert.DoesNotContain(active, t => t.Name == "execute_code");
        Assert.Contains(active, t => t.Name == "console_read");
        Assert.Contains(active, t => t.Name == "skill_manage");
        Assert.Contains(active, t => t.Name == "scene_manage");
        Assert.Contains(active, t => t.Name == "gameobject_manage");
    }

    [Fact]
    public void Load_testing_skill_adds_tools_unload_removes()
    {
        var rt = new ComdrRuntime();
        var before = rt.Registry.ActiveToolCount;

        Assert.True(rt.Registry.LoadSkill(SampleSkills.TestingSkillId));
        var afterLoad = rt.Registry.GetActiveTools();
        Assert.True(afterLoad.Count > before);
        Assert.Contains(afterLoad, t => t.Name == "tests_run");
        Assert.Contains(afterLoad, t => t.Name == "tests_list");

        Assert.True(rt.Registry.UnloadSkill(SampleSkills.TestingSkillId));
        var afterUnload = rt.Registry.GetActiveTools();
        Assert.Equal(before, afterUnload.Count);
        Assert.DoesNotContain(afterUnload, t => t.Name == "tests_run");
    }

    [Fact]
    public async Task Load_prefab_skill_and_list_skills_via_handler()
    {
        var rt = new ComdrRuntime();
        var list = await rt.Registry.CallAsync("skill_manage", JsonObjectFrom(("action", "list")));
        Assert.False(list.IsError);
        Assert.Contains(SampleSkills.TestingSkillId, list.Content);
        Assert.Contains(SampleSkills.PrefabAdvancedSkillId, list.Content);

        var load = await rt.Registry.CallAsync("skill_manage", JsonObjectFrom(
            ("action", "load"),
            ("id", SampleSkills.PrefabAdvancedSkillId)));
        Assert.False(load.IsError);
        Assert.Contains("prefab_batch_instantiate", load.Content);
        Assert.Contains(rt.Registry.GetActiveTools(), t => t.Name == "prefab_list");
    }

    [Fact]
    public async Task Escape_hatches_gated_until_enabled()
    {
        var rt = new ComdrRuntime();
        var denied = await rt.Registry.CallAsync("reflect_call", JsonObjectFrom(
            ("typeName", "System.String"),
            ("methodName", "IsNullOrEmpty")));
        Assert.True(denied.IsError);
        Assert.Contains("disabled", denied.Content, StringComparison.OrdinalIgnoreCase);

        var enable = await rt.Registry.CallAsync("escape_hatches_set", JsonObjectFrom(("enabled", true)));
        Assert.False(enable.IsError);
        Assert.True(rt.Registry.EscapeHatchesEnabled);

        var ok = await rt.Registry.CallAsync("reflect_call", JsonObjectFrom(
            ("typeName", "System.String"),
            ("methodName", "IsNullOrEmpty")));
        Assert.False(ok.IsError);
        Assert.Contains("dryRun", ok.Content);

        await rt.Registry.CallAsync("escape_hatches_set", JsonObjectFrom(("enabled", false)));
        Assert.DoesNotContain(rt.Registry.GetActiveTools(), t => t.Name == "execute_code");
    }

    private static System.Text.Json.Nodes.JsonObject JsonObjectFrom(params (string k, object v)[] pairs)
    {
        var o = new System.Text.Json.Nodes.JsonObject();
        foreach (var (k, v) in pairs)
        {
            o[k] = v switch
            {
                string s => s,
                bool b => b,
                int i => i,
                _ => v.ToString()
            };
        }
        return o;
    }
}
