using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.Editor;
using UnityComdr.Mcp;
using UnityComdr.Models;
using UnityComdr.Skills;
using UnityComdr.Tools;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// Wave-1 acceptance: drives shipped CoreTools / PromptCatalog / screenshot skill entry points.
/// </summary>
public class Wave1VisionAcceptanceTests
{
    [Fact]
    public async Task Component_modify_RectTransform_returns_layout_and_vision_nextStep()
    {
        var host = new InMemoryEditorHost();
        var go = host.CreateGameObject("UiPanel");
        host.AddComponent(go.Id, "RectTransform", new Dictionary<string, object?>
        {
            ["anchoredPosition"] = new { x = 10f, y = 20f },
            ["sizeDelta"] = new { x = 100f, y = 40f },
            ["anchorMin"] = new { x = 0f, y = 1f },
            ["anchorMax"] = new { x = 0f, y = 1f }
        });

        var rt = new ComdrRuntime(host);
        var result = await rt.Registry.CallAsync(
            "component_manage",
            Obj(
                ("action", "modify"),
                ("target", go.Id),
                ("type", "RectTransform"),
                ("properties", new JsonObject
                {
                    ["anchoredPosition"] = new JsonObject { ["x"] = 12, ["y"] = 24 },
                    ["sizeDelta"] = new JsonObject { ["x"] = 120, ["y"] = 48 }
                })));

        Assert.False(result.IsError, result.Content);
        Assert.Contains("layout", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vision", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("region", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("screenshot_capture", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Playmode_verify_prompt_embeds_vision_checkpoints()
    {
        var catalog = new PromptCatalog();
        var text = catalog.Get("playmode_verify_loop");
        Assert.Contains("type:image", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("screenshot_capture", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JUDGE FROM THE IMAGE", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hostMode=live", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("batch=surround", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scene_build_prompt_has_vision_checkpoint()
    {
        var text = new PromptCatalog().Get("scene_build_loop");
        Assert.Contains("screenshot_capture", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("image", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Screenshot_schema_documents_batch_surround_and_cost_knob()
    {
        var rt = new ComdrRuntime(new InMemoryEditorHost());
        await rt.Registry.CallAsync("skill_manage", Obj(("action", "load"), ("id", "screenshots")));
        var tool = rt.Registry.GetActiveTools().First(t => t.Name == "screenshot_capture");
        Assert.Contains("cost knob", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NATIVE", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("surround", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("type:image", tool.Description, StringComparison.OrdinalIgnoreCase);
        var schema = tool.InputSchema.ToJsonString();
        Assert.Contains("batch", schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("regionWidth", schema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Headless_screenshot_error_envelope_code_no_live_pixels()
    {
        var rt = new ComdrRuntime(new InMemoryEditorHost());
        await rt.Registry.CallAsync("skill_manage", Obj(("action", "load"), ("id", "screenshots")));
        var result = await rt.Registry.CallAsync(
            "screenshot_capture",
            Obj(("source", "game_view")));
        Assert.True(result.IsError);
        Assert.Contains("no_live_pixels", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payloadMarker", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject Obj(params (string k, object v)[] pairs)
    {
        var o = new JsonObject();
        foreach (var (k, v) in pairs)
        {
            o[k] = v switch
            {
                string s => s,
                JsonNode n => n,
                _ => JsonValue.Create(v)
            };
        }
        return o;
    }
}
