using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.Editor;
using UnityComdr.McpHost;
using UnityComdr.Models;
using UnityComdr.Skills;
using UnityComdr.Tools;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// Phase V vision protocol locks: MCP type:image (AC-V1) and honest headless blindness (AC-V5 / L1–L2).
/// These tests intentionally fail the old laziness (text+base64 / payloadMarker success).
/// </summary>
public class VisionProtocolTests
{
    // Minimal valid 1×1 PNG (red pixel).
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task ToolsCall_screenshot_capture_with_real_png_emits_mcp_image_content()
    {
        var host = new InMemoryEditorHost
        {
            ScreenshotOverride = (_, _, _, _, _, _, _, _, _) => new ScreenshotResult
            {
                Source = "game_view",
                Width = 1,
                Height = 1,
                Format = "png",
                IsRealPixels = true,
                OverlayUiIncluded = true,
                PngBase64 = TinyPngBase64,
                Note = "Fixture PNG for vision protocol test."
            }
        };
        var runtime = new ComdrRuntime(host);
        await runtime.Registry.CallAsync("skill_manage", Obj(("action", "load"), ("id", DomainSkills.ScreenshotsId)));
        var server = new McpServer(runtime, new StringReader(""), new StringWriter());

        var call = await server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"screenshot_capture","arguments":{"source":"game_view"}}}""");

        Assert.NotNull(call);
        Assert.Null(call!["error"]);
        Assert.False(call["result"]?["isError"]?.GetValue<bool>() ?? true);

        var content = call["result"]?["content"] as JsonArray;
        Assert.NotNull(content);
        Assert.Contains(content!, block => block?["type"]?.GetValue<string>() == "image");

        var image = content!.First(b => b?["type"]?.GetValue<string>() == "image");
        Assert.Equal("image/png", image?["mimeType"]?.GetValue<string>());
        Assert.Equal(TinyPngBase64, image?["data"]?.GetValue<string>());

        // Must not be text-only with embedded PNG (L1).
        var textBlocks = content!.Where(b => b?["type"]?.GetValue<string>() == "text").ToList();
        foreach (var t in textBlocks)
        {
            var text = t?["text"]?.GetValue<string>() ?? "";
            Assert.DoesNotContain(TinyPngBase64, text);
            Assert.DoesNotContain("pngBase64", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Headless_screenshot_capture_is_error_not_marker_success()
    {
        var runtime = new ComdrRuntime(new InMemoryEditorHost());
        await runtime.Registry.CallAsync("skill_manage", Obj(("action", "load"), ("id", DomainSkills.ScreenshotsId)));
        var server = new McpServer(runtime, new StringReader(""), new StringWriter());

        var call = await server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"screenshot_capture","arguments":{"source":"game_view"}}}""");

        Assert.NotNull(call);
        Assert.True(call!["result"]?["isError"]?.GetValue<bool>());
        var text = call["result"]?["content"]?[0]?["text"]?.GetValue<string>() ?? "";
        Assert.Contains("real pixels", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payloadMarker", text, StringComparison.OrdinalIgnoreCase);

        var content = call["result"]?["content"] as JsonArray;
        Assert.DoesNotContain(content!, block => block?["type"]?.GetValue<string>() == "image");
    }

    [Fact]
    public void ToolResult_OkWithImages_preserves_image_payload()
    {
        var result = ToolResult.OkWithImages(
            "{\"source\":\"game_view\"}",
            new[] { new ToolImageContent { MimeType = "image/png", DataBase64 = TinyPngBase64 } });
        Assert.False(result.IsError);
        Assert.NotNull(result.Images);
        Assert.Single(result.Images!);
        Assert.Equal(TinyPngBase64, result.Images![0].DataBase64);
        Assert.DoesNotContain(TinyPngBase64, result.Content);
    }

    private static JsonObject Obj(params (string k, string v)[] pairs)
    {
        var o = new JsonObject();
        foreach (var (k, v) in pairs) o[k] = v;
        return o;
    }
}
