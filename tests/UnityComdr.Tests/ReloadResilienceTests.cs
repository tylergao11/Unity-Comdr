using System.Diagnostics;
using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.Editor;
using UnityComdr.Mcp;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// Phase R / FR-R1–R2: Editor busy transitions return immediate actionable errors — never hang, never fake success.
/// </summary>
public class ReloadResilienceTests
{
    [Theory]
    [InlineData(EditorLifecyclePhases.EditorCompiling, "console_read")]
    [InlineData(EditorLifecyclePhases.EditorReloading, "script_list")]
    [InlineData(EditorLifecyclePhases.PlayTransition, "console_clear")]
    [InlineData(EditorLifecyclePhases.EditorGone, "hierarchy_get")]
    public async Task Tool_call_during_busy_returns_isError_with_phase_never_hangs(string phase, string tool)
    {
        var host = new InMemoryEditorHost();
        host.SimulateBusy(phase, suggestedRetrySeconds: 4);
        var rt = new ComdrRuntime(host);

        var sw = Stopwatch.StartNew();
        var result = await rt.Registry.CallAsync(tool, new JsonObject()).WaitAsync(TimeSpan.FromSeconds(2));
        sw.Stop();

        Assert.True(result.IsError, "busy tool call must be isError, not fake success");
        Assert.Contains(phase, result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("suggestedRetrySeconds=4", result.Content.Replace(" ", ""));
        Assert.Contains("nextStep=", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"must not hang; elapsed={sw.Elapsed}");
    }

    [Fact]
    public async Task Editor_state_reports_busy_phase_without_failing()
    {
        var host = new InMemoryEditorHost();
        host.SimulateBusy(EditorLifecyclePhases.EditorCompiling, suggestedRetrySeconds: 3);
        var rt = new ComdrRuntime(host);

        var state = await rt.Registry.CallAsync("editor_state", null);
        Assert.False(state.IsError, state.Content);
        Assert.Contains("\"phase\":\"editor_compiling\"", state.Content.Replace(" ", ""));
        Assert.Contains("\"suggestedRetrySeconds\":3", state.Content.Replace(" ", ""));
        Assert.Contains("\"isCompiling\":true", state.Content.Replace(" ", ""));
    }

    [Fact]
    public async Task ClearBusy_allows_tool_calls_again()
    {
        var host = new InMemoryEditorHost();
        host.SimulateBusy(EditorLifecyclePhases.EditorReloading);
        var rt = new ComdrRuntime(host);

        var blocked = await rt.Registry.CallAsync("console_read", null);
        Assert.True(blocked.IsError);
        Assert.Contains(EditorLifecyclePhases.EditorReloading, blocked.Content);

        host.ClearBusy();
        var ok = await rt.Registry.CallAsync("console_read", null);
        Assert.False(ok.IsError, ok.Content);
    }

    [Fact]
    public async Task SetCompiling_true_surfaces_editor_compiling_on_tools()
    {
        var host = new InMemoryEditorHost();
        host.SetCompiling(true);
        var rt = new ComdrRuntime(host);

        var result = await rt.Registry.CallAsync("console_read", null);
        Assert.True(result.IsError);
        Assert.Contains(EditorLifecyclePhases.EditorCompiling, result.Content);
    }

    [Fact]
    public void EditorBusyException_TryParse_roundtrips_phase_and_retry()
    {
        var msg = EditorLifecyclePhases.FormatBusyMessage(
            EditorLifecyclePhases.EditorReloading, 5);
        Assert.True(EditorBusyException.TryParse(msg, out var busy));
        Assert.NotNull(busy);
        Assert.Equal(EditorLifecyclePhases.EditorReloading, busy!.Phase);
        Assert.Equal(5, busy.SuggestedRetrySeconds);
        Assert.False(string.IsNullOrWhiteSpace(busy.NextStep));
    }

    [Fact]
    public void Code_fix_loop_prompt_teaches_retry_etiquette()
    {
        var catalog = new PromptCatalog();
        var text = catalog.Get("code_fix_loop");
        Assert.Contains("editor_compiling", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("editor_reloading", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("suggestedRetrySeconds", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("editor_state", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never treat", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Live_bridge_source_has_immediate_busy_and_lifecycle_hooks()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var livePath = Path.Combine(repoRoot, "packages", "com.unitycomdr.mcp", "Editor", "LiveUnityBridgeServer.cs");
        Assert.True(File.Exists(livePath), livePath);
        var src = File.ReadAllText(livePath);
        Assert.Contains("TryImmediateBusyResponse", src);
        Assert.Contains("editor_compiling", src);
        Assert.Contains("editor_reloading", src);
        Assert.Contains("play_transition", src);
        Assert.Contains("suggestedRetrySeconds", src);
        Assert.Contains("playModeStateChanged", src);
        Assert.Contains("beforeAssemblyReload", src);
        Assert.Contains("\\\"phase\\\":", src);
    }
}
