using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.Editor;
using UnityComdr.Models;
using UnityComdr.Skills;
using UnityComdr.Util;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// Regression tests for skeptic findings: scene isolation, partial transform, dig-deeper copy, skill error text.
/// </summary>
public class BugFixRegressionTests
{
    [Fact]
    public async Task CreateScene_isolates_gameobjects_from_previous_scene()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        // Seeded SampleScene has Main Camera
        var before = await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "get"), ("target", "Main Camera")));
        Assert.False(before.IsError);
        Assert.Contains("Main Camera", before.Content);

        var create = await rt.Registry.CallAsync("scene_manage", Obj(
            ("action", "create"),
            ("path", "Assets/Scenes/Level1.unity"),
            ("name", "Level1")));
        Assert.False(create.IsError);

        // Ghost objects must not leak into the new scene
        var ghost = await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "get"), ("target", "Main Camera")));
        Assert.True(ghost.IsError);
        Assert.Contains("Not found", ghost.Content);

        var hier = await rt.Registry.CallAsync("hierarchy_get", null);
        Assert.False(hier.IsError);
        Assert.DoesNotContain("Main Camera", hier.Content);
        Assert.Contains("\"nodeCount\":0", hier.Content.Replace(" ", ""));

        // Re-open original scene — camera returns
        await rt.Registry.CallAsync("scene_manage", Obj(
            ("action", "open"),
            ("path", "Assets/Scenes/SampleScene.unity")));
        var back = await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "get"), ("target", "Main Camera")));
        Assert.False(back.IsError);
        Assert.Contains("Main Camera", back.Content);
    }

    [Fact]
    public async Task SetTransform_partial_position_preserves_other_axes()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "create"), ("name", "Cube")));
        await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "set_transform"),
            ("target", "Cube"),
            ("position", new JsonObject { ["x"] = 1, ["y"] = 2, ["z"] = 3 })));

        await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "set_transform"),
            ("target", "Cube"),
            ("position", new JsonObject { ["y"] = 9 })));

        var get = await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "get"), ("target", "Cube")));
        Assert.False(get.IsError);
        // Expect position (1, 9, 3) — not (0, 9, 0). Parse structured transform.
        var node = JsonNode.Parse(get.Content) as JsonObject;
        Assert.NotNull(node);
        var pos = node!["transform"]?["position"] as JsonObject;
        Assert.NotNull(pos);
        Assert.Equal(1f, pos!["x"]!.GetValue<float>(), precision: 3);
        Assert.Equal(9f, pos["y"]!.GetValue<float>(), precision: 3);
        Assert.Equal(3f, pos["z"]!.GetValue<float>(), precision: 3);
    }

    [Fact]
    public void Hierarchy_digDeeper_names_real_tool()
    {
        var host = new InMemoryEditorHost();
        var summary = CompactResults.HierarchySummary(
            host.GetAllGameObjects(),
            host.GetActiveScene().RootObjectIds);
        var json = CompactResults.ToJson(summary);
        Assert.Contains("gameobject_manage", json);
        Assert.Contains("action=get", json);
        Assert.DoesNotContain("gameobject_get", json);
    }

    [Fact]
    public async Task Inactive_skill_tool_error_names_skill_manage()
    {
        var rt = new ComdrRuntime();
        var denied = await rt.Registry.CallAsync("tests_run", Obj(("mode", "EditMode")));
        Assert.True(denied.IsError);
        Assert.Contains("skill_manage", denied.Content);
        Assert.Contains("action=load", denied.Content);
        Assert.Contains(SampleSkills.TestingSkillId, denied.Content);
        Assert.DoesNotContain("skill_load", denied.Content);
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
                JsonNode jn => jn,
                _ => v.ToString()
            };
        }
        return o;
    }
}
