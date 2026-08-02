using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.Editor;
using UnityComdr.Models;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// Drives shipped handlers through ComdrRuntime + real InMemoryEditorHost (production interface).
/// </summary>
public class P0HandlerTests
{
    [Fact]
    public async Task Console_read_filters_injected_errors()
    {
        var host = new InMemoryEditorHost();
        host.AddConsoleLog(new ConsoleLogEntry(LogType.Log, "hello"));
        host.AddConsoleLog(new ConsoleLogEntry(LogType.Error, "NullReferenceException: boom", File: "Assets/Scripts/Player.cs", Line: 12));
        host.AddConsoleLog(new ConsoleLogEntry(LogType.Warning, "deprecated"));

        var rt = new ComdrRuntime(host);
        var result = await rt.Registry.CallAsync("console_read", Obj(("type", "Error")));
        Assert.False(result.IsError);
        Assert.Contains("NullReferenceException", result.Content);
        Assert.DoesNotContain("deprecated", result.Content);
        Assert.DoesNotContain("\"hello\"", result.Content);
    }

    [Fact]
    public async Task Script_write_read_compile_console_roundtrip()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        var write = await rt.Registry.CallAsync("script_write", Obj(
            ("path", "Assets/Scripts/Player.cs"),
            ("content", "using UnityEngine;\npublic class Player : MonoBehaviour { void Start() {} }\n")));
        Assert.False(write.IsError);

        var read = await rt.Registry.CallAsync("script_read", Obj(("path", "Assets/Scripts/Player.cs")));
        Assert.False(read.IsError);
        Assert.Contains("class Player", read.Content);

        host.AddConsoleLog(new ConsoleLogEntry(LogType.Error, "CS0246: missing type", File: "Assets/Scripts/Player.cs", Line: 1));
        var before = await rt.Registry.CallAsync("console_read", Obj(("type", "Error")));
        Assert.Contains("CS0246", before.Content);

        await rt.Registry.CallAsync("script_write", Obj(
            ("path", "Assets/Scripts/Player.cs"),
            ("content", "using UnityEngine;\npublic class Player : MonoBehaviour { public int hp = 10; }\n")));
        host.ClearConsole();
        await rt.Registry.CallAsync("editor_compile", null);
        var after = await rt.Registry.CallAsync("console_read", Obj(("type", "Error")));
        Assert.False(after.IsError);
        // Empty error list after clear + recompile
        Assert.Contains("\"total\":0", after.Content.Replace(" ", ""));
    }

    [Fact]
    public async Task Scene_create_and_gameobject_hierarchy()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        var scene = await rt.Registry.CallAsync("scene_manage", Obj(
            ("action", "create"),
            ("path", "Assets/Scenes/Level1.unity"),
            ("name", "Level1")));
        Assert.False(scene.IsError);
        Assert.Contains("Level1", scene.Content);

        var ground = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "create"),
            ("name", "Ground")));
        Assert.False(ground.IsError);

        var player = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "create"),
            ("name", "Player")));
        Assert.False(player.IsError);

        await rt.Registry.CallAsync("component_manage", Obj(
            ("action", "add"),
            ("target", "Player"),
            ("type", "Rigidbody")));

        await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "set_transform"),
            ("target", "Player"),
            ("position", new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0 })));

        var hier = await rt.Registry.CallAsync("hierarchy_get", Obj(("maxDepth", 2), ("maxNodes", 40)));
        Assert.False(hier.IsError);
        Assert.Contains("Player", hier.Content);
        Assert.Contains("Ground", hier.Content);
        Assert.Contains("digDeeper", hier.Content);

        var get = await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "get"), ("target", "Player")));
        Assert.Contains("Rigidbody", get.Content);
    }

    [Fact]
    public async Task Material_and_prefab_basic_ops()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "create"), ("name", "Cube")));
        await rt.Registry.CallAsync("component_manage", Obj(
            ("action", "add"), ("target", "Cube"), ("type", "MeshRenderer")));

        var mat = await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "material_create"),
            ("path", "Assets/Materials/Red.mat"),
            ("color", "#FF0000")));
        Assert.False(mat.IsError);
        Assert.Contains("Red", mat.Content);

        var assign = await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "material_assign"),
            ("target", "Cube"),
            ("path", "Assets/Materials/Red.mat")));
        Assert.False(assign.IsError);

        var prefab = await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "prefab_create"),
            ("path", "Assets/Prefabs/Cube.prefab"),
            ("target", "Cube")));
        Assert.False(prefab.IsError);

        var inst = await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "prefab_instantiate"),
            ("path", "Assets/Prefabs/Cube.prefab")));
        Assert.False(inst.IsError);

        var find = await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "find"),
            ("kind", "Prefab")));
        Assert.Contains("Cube.prefab", find.Content);
    }

    [Fact]
    public async Task Compact_pagination_has_more_and_dig_deeper()
    {
        var host = new InMemoryEditorHost();
        for (var i = 0; i < 25; i++)
            host.AddConsoleLog(new ConsoleLogEntry(LogType.Log, $"msg-{i}"));

        var rt = new ComdrRuntime(host);
        var page = await rt.Registry.CallAsync("console_read", Obj(
            ("pageSize", 10),
            ("offset", 0)));
        Assert.Contains("\"hasMore\":true", page.Content.Replace(" ", ""));
        Assert.Contains("digDeeper", page.Content);
        Assert.Contains("msg-0", page.Content);
        Assert.DoesNotContain("msg-20", page.Content);
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
