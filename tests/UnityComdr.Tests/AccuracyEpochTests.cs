using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.Editor;
using UnityComdr.Models;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// O1/O2 accuracy debts (borrow-plan §3): compile epoch + sessionGeneration / stale_reference.
/// </summary>
public class AccuracyEpochTests
{
    [Fact]
    public async Task Editor_compile_bumps_compileEpoch()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        var before = host.GetState();
        Assert.Equal(0, before.CompileEpoch);

        var compile = await rt.Registry.CallAsync("editor_compile", null);
        Assert.False(compile.IsError, compile.Content);
        Assert.Contains("\"compileEpoch\":1", compile.Content.Replace(" ", ""));

        var after = host.GetState();
        Assert.Equal(1, after.CompileEpoch);

        await rt.Registry.CallAsync("editor_compile", null);
        Assert.Equal(2, host.GetState().CompileEpoch);
    }

    [Fact]
    public async Task Console_read_after_compile_marks_older_epoch_entries_stale()
    {
        var host = new InMemoryEditorHost();
        host.AddConsoleLog(new ConsoleLogEntry(LogType.Error, "NullReferenceException: pre-compile boom"));
        var rt = new ComdrRuntime(host);

        var before = await rt.Registry.CallAsync("console_read", Obj(("type", "Error")));
        Assert.False(before.IsError, before.Content);
        Assert.Contains("\"epoch\":0", before.Content.Replace(" ", ""));
        Assert.Contains("\"stale\":false", before.Content.Replace(" ", ""));

        var compile = await rt.Registry.CallAsync("editor_compile", null);
        Assert.Contains("\"compileEpoch\":1", compile.Content.Replace(" ", ""));

        var after = await rt.Registry.CallAsync("console_read", null);
        Assert.False(after.IsError, after.Content);
        // Pre-compile error survives (not a CS error) and is marked stale against the new epoch.
        Assert.Contains("pre-compile boom", after.Content);
        Assert.Contains("\"stale\":true", after.Content.Replace(" ", ""));
        Assert.Contains("Scripts recompiled", after.Content);
        Assert.Contains("\"epoch\":1", after.Content.Replace(" ", ""));
    }

    [Fact]
    public void BumpSessionGeneration_increments_and_stale_id_throws_actionable_error()
    {
        var host = new InMemoryEditorHost();
        var go = host.CreateGameObject("AccuracyCube");
        var oldId = go.Id;
        Assert.Equal(1, host.GetState().SessionGeneration);

        host.BumpSessionGeneration();
        Assert.Equal(2, host.GetState().SessionGeneration);

        var ex = Assert.Throws<InvalidOperationException>(() => host.FindGameObject(oldId));
        Assert.Contains("stale_reference", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sessionGeneration", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Path-based re-find remains valid.
        var byPath = host.FindGameObject("AccuracyCube");
        Assert.NotNull(byPath);
        Assert.NotEqual(oldId, byPath!.Id);
    }

    [Fact]
    public async Task Gameobject_manage_with_stale_id_returns_tool_error()
    {
        var host = new InMemoryEditorHost();
        var go = host.CreateGameObject("StaleTarget");
        var oldId = go.Id;
        host.BumpSessionGeneration();
        var rt = new ComdrRuntime(host);

        var result = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "get"),
            ("target", oldId)));
        Assert.True(result.IsError);
        Assert.Contains("stale_reference", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sessionGeneration", result.Content, StringComparison.OrdinalIgnoreCase);

        var ok = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "get"),
            ("target", "StaleTarget")));
        Assert.False(ok.IsError, ok.Content);
        Assert.Contains("StaleTarget", ok.Content);
    }

    [Fact]
    public void Editor_state_surfaces_compileEpoch_and_sessionGeneration()
    {
        var host = new InMemoryEditorHost();
        host.RequestScriptCompile();
        host.BumpSessionGeneration();
        var state = host.GetState();
        Assert.Equal(1, state.CompileEpoch);
        Assert.Equal(2, state.SessionGeneration);
    }

    [Fact]
    public void Live_bridge_source_exposes_epoch_and_sessionGeneration_hooks()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var livePath = Path.Combine(repoRoot, "packages", "com.unitycomdr.mcp", "Editor", "LiveUnityBridgeServer.cs");
        Assert.True(File.Exists(livePath), livePath);
        var src = File.ReadAllText(livePath);
        Assert.Contains("sessionGeneration", src);
        Assert.Contains("compileEpoch", src);
        Assert.Contains("stale_reference", src);
        Assert.Contains("afterAssemblyReload", src);
        Assert.Contains("BumpCompileEpoch", src);
        Assert.Contains("GetSessionGeneration", src);
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
