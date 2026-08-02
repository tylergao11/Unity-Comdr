using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.Editor;
using UnityComdr.Models;
using UnityComdr.Skills;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// Full-flow acceptance: code-fix, scene-build, playmode-verify on the **shipped**
/// registry + <see cref="IEditorHost"/> path (same handlers agents call).
/// </summary>
public class FullLoopWorkflowTests
{
    [Fact]
    public async Task FullLoop_CodeFix_planted_error_cleared_after_write_and_compile()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        // Plant a compile-style error referencing a script.
        host.AddConsoleLog(new ConsoleLogEntry(
            LogType.Error,
            "Assets/Scripts/Broken.cs(3,1): error CS1002: ; expected",
            File: "Assets/Scripts/Broken.cs",
            Line: 3));

        var before = await rt.Registry.CallAsync("console_read", Obj(("type", "Error")));
        Assert.False(before.IsError);
        Assert.Contains("CS1002", before.Content);
        Assert.DoesNotContain("\"total\":0", before.Content.Replace(" ", ""));

        var readMissing = await rt.Registry.CallAsync("script_read", Obj(("path", "Assets/Scripts/Broken.cs")));
        Assert.True(readMissing.IsError);

        var write = await rt.Registry.CallAsync("script_write", Obj(
            ("path", "Assets/Scripts/Broken.cs"),
            ("content", "using UnityEngine;\npublic class Broken : MonoBehaviour { public int value = 1; }\n")));
        Assert.False(write.IsError);

        var readOk = await rt.Registry.CallAsync("script_read", Obj(("path", "Assets/Scripts/Broken.cs")));
        Assert.False(readOk.IsError);
        Assert.Contains("class Broken", readOk.Content);

        // Must NOT console_clear — write/compile path must clear file-scoped compile errors.
        // After script_write, host drops errors that reference this path; recompile clears remaining CS* errors.
        var mid = await rt.Registry.CallAsync("console_read", Obj(("type", "Error")));
        Assert.False(mid.IsError);
        Assert.DoesNotContain("CS1002", mid.Content);

        // Plant a second compile error not yet fixed by write, then compile must clear CS-style errors.
        host.AddConsoleLog(new ConsoleLogEntry(
            LogType.Error,
            "Assets/Scripts/Other.cs(1,1): error CS0246: type not found",
            File: "Assets/Scripts/Other.cs",
            Line: 1));
        // Other.cs does not exist as a written script — still a CS error; recompile clears CS* class errors.
        var compile = await rt.Registry.CallAsync("editor_compile", null);
        Assert.False(compile.IsError);

        var after = await rt.Registry.CallAsync("console_read", Obj(("type", "Error")));
        Assert.False(after.IsError);
        Assert.Contains("\"total\":0", after.Content.Replace(" ", ""));
        Assert.DoesNotContain("CS1002", after.Content);
        Assert.DoesNotContain("CS0246", after.Content);

        var state = await rt.Registry.CallAsync("editor_state", null);
        Assert.Contains("\"isCompiling\":false", state.Content.Replace(" ", ""));
    }

    [Fact]
    public async Task FullLoop_SceneBuild_create_go_material_prefab_hierarchy_and_isolation()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        var create = await rt.Registry.CallAsync("scene_manage", Obj(
            ("action", "create"),
            ("path", "Assets/Scenes/FullFlowLevel.unity"),
            ("name", "FullFlowLevel")));
        Assert.False(create.IsError);
        Assert.Contains("FullFlowLevel", create.Content);

        // Prior scene seed (Main Camera) must not leak into this empty scene.
        var ghost = await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "get"), ("target", "Main Camera")));
        Assert.True(ghost.IsError);

        var ground = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "create"),
            ("name", "Ground"),
            ("primitive", "Plane")));
        Assert.False(ground.IsError);

        var player = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "create"),
            ("name", "Player"),
            ("primitive", "Capsule"),
            ("tag", "Player")));
        Assert.False(player.IsError);

        await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "set_transform"),
            ("target", "Player"),
            ("position", new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0 })));

        await rt.Registry.CallAsync("component_manage", Obj(
            ("action", "add"),
            ("target", "Player"),
            ("type", "Rigidbody")));

        var mat = await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "material_create"),
            ("path", "Assets/Materials/FullFlow.mat"),
            ("color", "#00FF00")));
        Assert.False(mat.IsError);

        await rt.Registry.CallAsync("component_manage", Obj(
            ("action", "add"),
            ("target", "Player"),
            ("type", "MeshRenderer")));

        var assign = await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "material_assign"),
            ("target", "Player"),
            ("path", "Assets/Materials/FullFlow.mat")));
        Assert.False(assign.IsError);

        var prefab = await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "prefab_create"),
            ("path", "Assets/Prefabs/Player.prefab"),
            ("target", "Player")));
        Assert.False(prefab.IsError);

        var hier = await rt.Registry.CallAsync("hierarchy_get", Obj(("maxDepth", 3), ("maxNodes", 40)));
        Assert.False(hier.IsError);
        Assert.Contains("Player", hier.Content);
        Assert.Contains("Ground", hier.Content);
        Assert.Contains("digDeeper", hier.Content);
        Assert.Contains("gameobject_manage", hier.Content);

        var find = await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "find"), ("tag", "Player")));
        Assert.Contains("Player", find.Content);

        var save = await rt.Registry.CallAsync("scene_manage", Obj(("action", "save")));
        Assert.False(save.IsError);

        // Second scene isolation
        await rt.Registry.CallAsync("scene_manage", Obj(
            ("action", "create"),
            ("path", "Assets/Scenes/OtherLevel.unity"),
            ("name", "OtherLevel")));
        var missing = await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "get"), ("target", "Player")));
        Assert.True(missing.IsError);

        // Re-open first scene — objects return
        await rt.Registry.CallAsync("scene_manage", Obj(
            ("action", "open"),
            ("path", "Assets/Scenes/FullFlowLevel.unity")));
        var back = await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "get"), ("target", "Player")));
        Assert.False(back.IsError);
        Assert.Contains("Rigidbody", back.Content);
    }

    [Fact]
    public async Task FullLoop_PlaymodeVerify_skill_play_pause_stop_step_and_screenshot()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        // Core budget before skill load
        Assert.True(rt.Registry.ActiveToolCount <= Tools.ToolBudget.MaxDefaultCoreTools);

        var loadPm = await rt.Registry.CallAsync("skill_manage", Obj(("action", "load"), ("id", DomainSkills.PlayModeId)));
        Assert.False(loadPm.IsError);
        Assert.Contains(rt.Registry.GetActiveTools(), t => t.Name == "playmode_control");

        var play = await rt.Registry.CallAsync("playmode_control", Obj(("action", "play")));
        Assert.False(play.IsError);
        var statePlay = await rt.Registry.CallAsync("editor_state", null);
        Assert.Contains("\"isPlaying\":true", statePlay.Content.Replace(" ", ""));
        Assert.Contains("\"isPaused\":false", statePlay.Content.Replace(" ", ""));

        var pause = await rt.Registry.CallAsync("playmode_control", Obj(("action", "pause")));
        Assert.False(pause.IsError);
        var statePause = await rt.Registry.CallAsync("editor_state", null);
        Assert.Contains("\"isPlaying\":true", statePause.Content.Replace(" ", ""));
        Assert.Contains("\"isPaused\":true", statePause.Content.Replace(" ", ""));

        var step = await rt.Registry.CallAsync("playmode_control", Obj(("action", "step")));
        Assert.False(step.IsError);

        var stop = await rt.Registry.CallAsync("playmode_control", Obj(("action", "stop")));
        Assert.False(stop.IsError);
        var stateStop = await rt.Registry.CallAsync("editor_state", null);
        Assert.Contains("\"isPlaying\":false", stateStop.Content.Replace(" ", ""));

        var loadShot = await rt.Registry.CallAsync("skill_manage", Obj(("action", "load"), ("id", DomainSkills.ScreenshotsId)));
        Assert.False(loadShot.IsError);
        var shot = await rt.Registry.CallAsync("screenshot_capture", Obj(("source", "game_view")));
        Assert.False(shot.IsError);
        Assert.False(string.IsNullOrWhiteSpace(shot.Content));
        Assert.Contains("payloadMarker", shot.Content);

        // Unload keeps budget discipline
        await rt.Registry.CallAsync("skill_manage", Obj(("action", "unload"), ("id", DomainSkills.PlayModeId)));
        await rt.Registry.CallAsync("skill_manage", Obj(("action", "unload"), ("id", DomainSkills.ScreenshotsId)));
        Assert.True(rt.Registry.ActiveToolCount <= Tools.ToolBudget.MaxDefaultCoreTools);
    }

    [Fact]
    public async Task CodeFix_recompile_clears_cs_errors_without_console_clear()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);
        host.AddConsoleLog(new ConsoleLogEntry(LogType.Error, "error CS0001: boom", File: "Assets/Scripts/A.cs"));
        host.AddConsoleLog(new ConsoleLogEntry(LogType.Log, "ok keep me"));
        // No console_clear — only compile
        await rt.Registry.CallAsync("editor_compile", null);
        var errors = await rt.Registry.CallAsync("console_read", Obj(("type", "Error")));
        Assert.Contains("\"total\":0", errors.Content.Replace(" ", ""));
        var all = await rt.Registry.CallAsync("console_read", null);
        Assert.Contains("ok keep me", all.Content);
    }

    [Fact]
    public void Live_bridge_structural_proof_and_factory_fallback()
    {
        // Structural: package live entry source exists
        // bin/Release/net8.0 → 5×.. → repo root
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var liveServer = Path.Combine(repoRoot, "packages", "com.unitycomdr.mcp", "Editor", "LiveUnityBridgeServer.cs");
        Assert.True(File.Exists(liveServer), $"Missing live bridge server: {liveServer}");
        var src = File.ReadAllText(liveServer);
        Assert.Contains("LiveUnityBridgeServer", src);
        Assert.Contains("InitializeOnLoad", src);
        Assert.Contains("TcpListener", src);
        var clientPath = Path.Combine(repoRoot, "src", "UnityComdr.Core", "Editor", "BridgeClientEditorHost.cs");
        Assert.True(File.Exists(clientPath), $"Missing bridge client: {clientPath}");
        Assert.Contains("BridgeClientEditorHost", File.ReadAllText(clientPath));
        Assert.Contains("IEditorHost", File.ReadAllText(clientPath));

        // Factory falls back to headless when no Unity bridge is listening
        Environment.SetEnvironmentVariable(EditorHostFactory.EnvForceHeadless, "1");
        try
        {
            var sel = EditorHostFactory.CreateFromEnvironment();
            Assert.Equal(EditorHostMode.HeadlessInMemory, sel.Mode);
            Assert.IsType<InMemoryEditorHost>(sel.Host);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EditorHostFactory.EnvForceHeadless, null);
        }

        // Without force flag, no bridge → still headless (honest fallback)
        Environment.SetEnvironmentVariable(EditorHostFactory.EnvForceHeadless, null);
        var auto = EditorHostFactory.CreateFromEnvironment();
        Assert.Equal(EditorHostMode.HeadlessInMemory, auto.Mode);
        Assert.Contains("InMemory", auto.Detail, StringComparison.OrdinalIgnoreCase);
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
