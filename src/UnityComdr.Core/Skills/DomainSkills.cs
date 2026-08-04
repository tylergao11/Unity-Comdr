using System.Text.Json.Nodes;
using UnityComdr.Editor;
using UnityComdr.Tools;
using UnityComdr.Util;

namespace UnityComdr.Skills;

/// <summary>
/// Domain skills mapping popular Unity MCP advantages onto on-demand tool packs.
/// Default session stays ≤15 tools; agents load these via skill_manage.
/// </summary>
public static class DomainSkills
{
    public const string PlayModeId = "playmode";
    public const string SelectionId = "selection";
    public const string PackagesId = "packages";
    public const string MenuId = "menu";
    public const string ProfilingId = "profiling";
    public const string ScreenshotsId = "screenshots";
    public const string BatchId = "batch";
    public const string TestingId = SampleSkills.TestingSkillId;
    public const string PrefabAdvancedId = SampleSkills.PrefabAdvancedSkillId;

    public static void RegisterAll(ToolRegistry registry, IEditorHost editor)
    {
        SampleSkills.RegisterAll(registry, editor);
        registry.RegisterSkill(BuildPlayMode(editor));
        registry.RegisterSkill(BuildSelection(editor));
        registry.RegisterSkill(BuildPackages(editor));
        registry.RegisterSkill(BuildMenu(editor));
        registry.RegisterSkill(BuildProfiling(editor));
        registry.RegisterSkill(BuildScreenshots(editor));
        registry.RegisterSkill(BuildBatch(registry, editor));
    }

    public static IReadOnlyList<(string Id, string Name, string Description, string ParitySource)> CatalogMeta() =>
        new (string, string, string, string)[]
        {
            (TestingId, "Testing", "Live TestRunnerApi job (tests_run → tests_status); headless isError", "Coplay RunTests/GetTestJob + Unity TestRunnerApi"),
            (PrefabAdvancedId, "Prefab Advanced", "Batch instantiate & prefab listing", "IvanMurzak prefab suite / AnkleBreaker breadth"),
            (PlayModeId, "Play Mode", "play|pause|stop|step", "CoderGamester set_play_mode / IvanMurzak editor-application-set-state"),
            (SelectionId, "Selection", "get/set Editor selection", "IvanMurzak editor-selection-*"),
            (PackagesId, "Package Manager", "list/add/remove/search UPM packages", "IvanMurzak package-* / CoderGamester add_package"),
            (MenuId, "Menu Items", "list & execute Unity menu items", "CoderGamester execute_menu_item"),
            (ProfilingId, "Profiling", "profiler start/stop/snapshot/save/load", "IvanMurzak profiler-*"),
            (ScreenshotsId, "Screenshots", "camera/game/scene/isolated captures", "IvanMurzak screenshot-*"),
            (BatchId, "Batch Execute", "run multiple tool calls in one request", "CoderGamester batch_execute / Coplay multi-step")
        };

    private static SkillDefinition BuildPlayMode(IEditorHost editor) => new()
    {
        Id = PlayModeId,
        Name = "Play Mode",
        Description = "Control Unity Play Mode: play, pause, stop, step (CoderGamester/IvanMurzak parity).",
        Tools = new[]
        {
            Tool("playmode_control", PlayModeId,
                "Control play mode. Action: get|play|pause|stop|step.",
                JsonSchemaHelper.Object(
                    ("action", JsonSchemaHelper.String(null, new[] { "get", "play", "pause", "stop", "step" }), true)
                ),
                async (args, _) =>
                {
                    var action = Arg(args, "action")?.ToLowerInvariant() ?? "get";
                    switch (action)
                    {
                        case "get":
                            return ToolResult.OkJson(editor.GetState());
                        case "play":
                            editor.SetPlayMode(true, false);
                            return ToolResult.OkJson(editor.GetState());
                        case "pause":
                            editor.SetPlayMode(true, true);
                            return ToolResult.OkJson(editor.GetState());
                        case "stop":
                            editor.SetPlayMode(false);
                            return ToolResult.OkJson(editor.GetState());
                        case "step":
                            editor.StepPlayModeFrame();
                            return ToolResult.OkJson(editor.GetState());
                        default:
                            return ToolResult.Error($"Unknown action: {action}");
                    }
                })
        }
    };

    private static SkillDefinition BuildSelection(IEditorHost editor) => new()
    {
        Id = SelectionId,
        Name = "Selection",
        Description = "Get or set Editor selection (GameObjects and assets).",
        Tools = new[]
        {
            Tool("selection_manage", SelectionId,
                "Action: get|set. For set, pass gameObjectIds and/or assetPaths arrays.",
                JsonSchemaHelper.Object(
                    ("action", JsonSchemaHelper.String(null, new[] { "get", "set" }), true),
                    ("gameObjectIds", JsonSchemaHelper.ObjectOpen("Array-like object or use comma field goIds"), false),
                    ("goIds", JsonSchemaHelper.String("Comma-separated GameObject ids"), false),
                    ("assetPaths", JsonSchemaHelper.String("Comma-separated asset paths"), false)
                ),
                async (args, _) =>
                {
                    var action = Arg(args, "action")?.ToLowerInvariant() ?? "get";
                    if (action == "get")
                        return ToolResult.OkJson(editor.GetSelection());
                    if (action == "set")
                    {
                        var goIds = SplitCsv(Arg(args, "goIds"));
                        var assets = SplitCsv(Arg(args, "assetPaths"));
                        editor.SetSelection(goIds, assets);
                        return ToolResult.OkJson(editor.GetSelection());
                    }
                    return ToolResult.Error($"Unknown action: {action}");
                })
        }
    };

    private static SkillDefinition BuildPackages(IEditorHost editor) => new()
    {
        Id = PackagesId,
        Name = "Package Manager",
        Description = "UPM via UnityEditor.PackageManager.Client (live only).",
        Tools = new[]
        {
            Tool("package_manage", PackagesId,
                "Action: list|add|remove|search via PackageManager.Client. Requires hostMode=live. Headless returns isError (no manifest fake).",
                JsonSchemaHelper.Object(
                    ("action", JsonSchemaHelper.String(null, new[] { "list", "add", "remove", "search" }), true),
                    ("package", JsonSchemaHelper.String("Package id, url, or name@version"), false),
                    ("query", JsonSchemaHelper.String("Search query"), false)
                ),
                async (args, _) =>
                {
                    if (!IsLive(editor))
                        return ToolResult.ErrorEnvelope(
                            "requires_live",
                            "package_manage requires hostMode=live PackageManager.Client. Headless does not fake UPM.",
                            nextStep: "Open Unity with Unity-Comdr bridge (editor_state.hostMode=live).");

                    var action = Arg(args, "action")?.ToLowerInvariant() ?? "list";
                    try
                    {
                        switch (action)
                        {
                            case "list":
                                return ToolResult.OkJson(new { hostMode = editor.HostMode, packages = editor.ListPackages() });
                            case "add":
                            {
                                var pkg = Arg(args, "package") ?? throw new ArgumentException("package required");
                                return ToolResult.OkJson(new { hostMode = editor.HostMode, package = editor.AddPackage(pkg) });
                            }
                            case "remove":
                            {
                                var pkg = Arg(args, "package") ?? throw new ArgumentException("package required");
                                return editor.RemovePackage(pkg)
                                    ? ToolResult.OkJson(new { removed = pkg, hostMode = editor.HostMode })
                                    : ToolResult.Error($"Package not found: {pkg}");
                            }
                            case "search":
                            {
                                var q = Arg(args, "query") ?? "";
                                return ToolResult.OkJson(new { hostMode = editor.HostMode, results = editor.SearchPackages(q) });
                            }
                            default:
                                return ToolResult.Error($"Unknown action: {action}");
                        }
                    }
                    catch (Exception ex)
                    {
                        return ToolResult.ErrorEnvelope("package_error", ex.Message,
                            nextStep: "Check package id and Unity Package Manager status.");
                    }
                })
        }
    };

    private static SkillDefinition BuildMenu(IEditorHost editor) => new()
    {
        Id = MenuId,
        Name = "Menu Items",
        Description = "List (curated whitelist, not full Unity menu tree) and execute menu items.",
        Tools = new[]
        {
            Tool("menu_manage", MenuId,
                "Action: list|execute. list returns a curated whitelist only (coverage LIMITED — not all Unity menus). execute uses EditorApplication.ExecuteMenuItem on live.",
                JsonSchemaHelper.Object(
                    ("action", JsonSchemaHelper.String(null, new[] { "list", "execute" }), true),
                    ("path", JsonSchemaHelper.String("Menu path e.g. GameObject/Create Empty"), false),
                    ("filter", JsonSchemaHelper.String("Filter menu list"), false)
                ),
                async (args, _) =>
                {
                    var action = Arg(args, "action")?.ToLowerInvariant() ?? "list";
                    if (action == "list")
                        return ToolResult.OkJson(new
                        {
                            coverage = "whitelist",
                            note = "Not a complete Unity menu catalog — curated paths only.",
                            hostMode = editor.HostMode,
                            items = editor.ListMenuItems(Arg(args, "filter"))
                        });
                    if (action == "execute")
                    {
                        var path = Arg(args, "path") ?? throw new ArgumentException("path required");
                        return editor.ExecuteMenuItem(path)
                            ? ToolResult.OkJson(new { executed = path, state = editor.GetState() })
                            : ToolResult.Error($"Menu execute failed or path unknown: {path}");
                    }
                    return ToolResult.Error($"Unknown action: {action}");
                })
        }
    };

    private static SkillDefinition BuildProfiling(IEditorHost editor) => new()
    {
        Id = ProfilingId,
        Name = "Profiling",
        Description = "Memory/FPS metrics snapshot (Profiler.Get* counters). save/load is JSON metrics snapshot — NOT Unity Profiler .data binary.",
        Tools = new[]
        {
            Tool("profiler_manage", ProfilingId,
                "Action: start|stop|status|capture|clear|save|load. save/load store JSON metrics snapshots only (not official Profiler session files).",
                JsonSchemaHelper.Object(
                    ("action", JsonSchemaHelper.String(null, new[]
                    {
                        "start", "stop", "status", "capture", "clear", "save", "load"
                    }), true),
                    ("path", JsonSchemaHelper.String("JSON metrics snapshot path for save/load"), false)
                ),
                async (args, _) =>
                {
                    var action = Arg(args, "action")?.ToLowerInvariant() ?? "status";
                    switch (action)
                    {
                        case "start":
                            editor.SetProfilerEnabled(true);
                            return ToolResult.OkJson(WrapProfiler(editor));
                        case "stop":
                            editor.SetProfilerEnabled(false);
                            return ToolResult.OkJson(WrapProfiler(editor));
                        case "status":
                        case "capture":
                            return ToolResult.OkJson(WrapProfiler(editor));
                        case "clear":
                            editor.ClearProfilerData();
                            return ToolResult.OkJson(new
                            {
                                cleared = true,
                                note = "Counters reset; not a full Profiler window clear.",
                                hostMode = editor.HostMode
                            });
                        case "save":
                        {
                            var path = Arg(args, "path") ?? "Temp/unity-comdr-profiler-metrics.json";
                            editor.SaveProfilerData(path);
                            return ToolResult.OkJson(new
                            {
                                saved = path,
                                format = "json-metrics-snapshot",
                                note = "Not a Unity Profiler binary capture.",
                                hostMode = editor.HostMode
                            });
                        }
                        case "load":
                        {
                            var path = Arg(args, "path") ?? throw new ArgumentException("path required");
                            var snap = editor.LoadProfilerData(path);
                            return snap == null
                                ? ToolResult.Error($"Metrics snapshot not found: {path}")
                                : ToolResult.OkJson(new
                                {
                                    format = "json-metrics-snapshot",
                                    snapshot = snap,
                                    hostMode = editor.HostMode
                                });
                        }
                        default:
                            return ToolResult.Error($"Unknown action: {action}");
                    }
                })
        }
    };

    private static SkillDefinition BuildScreenshots(IEditorHost editor) => new()
    {
        Id = ScreenshotsId,
        Name = "Screenshots",
        Description = "Capture camera / game view / scene view / isolated GO. Returns MCP image content when live pixels are available.",
        Tools = new[]
        {
            Tool("screenshot_capture", ScreenshotsId,
                "Capture real pixels as MCP type:image (png). " +
                "source: camera|game_view|scene_view|isolated. " +
                "maxResolution default 640 is a WHOLE-FRAME cost knob only (not accuracy license). " +
                "regionX/Y/Width/Height crops stay NATIVE resolution (AC-V9). " +
                "batch=surround returns ONE labeled 6-angle contact sheet (AC-V7; requires target). " +
                "game_view without target includes Overlay UI; camera path excludes overlay (see overlayUiIncluded). " +
                "headless/no live Editor → isError (never marker success).",
                JsonSchemaHelper.Object(
                    ("source", JsonSchemaHelper.String(null, new[] { "camera", "game_view", "scene_view", "isolated" }), true),
                    ("target", JsonSchemaHelper.String("Camera or GO id for camera/isolated/surround"), false),
                    ("width", JsonSchemaHelper.Integer("Capture buffer width hint (camera path)"), false),
                    ("height", JsonSchemaHelper.Integer("Capture buffer height hint (camera path)"), false),
                    ("maxResolution", JsonSchemaHelper.Integer("Whole-frame longest-edge cap (default 640 cost knob). Ignored for region crops."), false),
                    ("regionX", JsonSchemaHelper.Integer("Crop X (top-left). Native resolution — no 640 downscale."), false),
                    ("regionY", JsonSchemaHelper.Integer("Crop Y (top-left). Native resolution — no 640 downscale."), false),
                    ("regionWidth", JsonSchemaHelper.Integer("Crop width px (native)."), false),
                    ("regionHeight", JsonSchemaHelper.Integer("Crop height px (native)."), false),
                    ("batch", JsonSchemaHelper.String("none (default) or surround = single contact sheet", new[] { "none", "surround" }), false)
                ),
                async (args, _) =>
                {
                    var source = Arg(args, "source") ?? "game_view";
                    var w = IntArg(args, "width") ?? 1280;
                    var h = IntArg(args, "height") ?? 720;
                    var maxRes = IntArg(args, "maxResolution") ?? 640;
                    var regionX = IntArg(args, "regionX");
                    var regionY = IntArg(args, "regionY");
                    var regionW = IntArg(args, "regionWidth");
                    var regionH = IntArg(args, "regionHeight");
                    var batch = Arg(args, "batch") ?? "none";
                    try
                    {
                        var result = editor.CaptureScreenshot(
                            source, Arg(args, "target"), w, h, maxRes, regionX, regionY, regionW, regionH, batch);

                        if (result.IsRealPixels && !string.IsNullOrEmpty(result.PngBase64))
                        {
                            var meta = new
                            {
                                source = result.Source,
                                format = result.Format ?? "png",
                                width = result.Width,
                                height = result.Height,
                                filePath = result.FilePath,
                                isRealPixels = true,
                                overlayUiIncluded = result.OverlayUiIncluded,
                                batch = result.Batch ?? batch,
                                regionNative = result.RegionNative,
                                wholeFrameDownscaled = result.WholeFrameDownscaled,
                                maxResolutionApplied = maxRes,
                                note = result.Note,
                                targetId = result.TargetId,
                                mimeType = "image/png",
                                contentType = "image"
                            };
                            return ToolResult.OkWithImages(
                                System.Text.Json.JsonSerializer.Serialize(meta, CompactResults.JsonOptions),
                                new[]
                                {
                                    new ToolImageContent
                                    {
                                        MimeType = "image/png",
                                        DataBase64 = result.PngBase64!
                                    }
                                },
                                structured: meta);
                        }

                        return ToolResult.ErrorEnvelope(
                            "no_live_pixels",
                            result.Note
                            ?? "No real pixels available (no live Editor / no camera / capture failed).",
                            suggestion: "Vision requires hostMode=live and a capturable view.",
                            nextStep: "Open Unity with Unity-Comdr bridge; call editor_state and confirm hostMode=live; retry screenshot_capture.");
                    }
                    catch (Exception ex)
                    {
                        return ToolResult.ErrorEnvelope(
                            "screenshot_failed",
                            ex.Message,
                            nextStep: "Fix camera/Scene View/target, or use source=game_view with an open Game View.");
                    }
                })
        }
    };

    private static SkillDefinition BuildBatch(ToolRegistry registry, IEditorHost editor) => new()
    {
        Id = BatchId,
        Name = "Batch Execute",
        Description = "Execute multiple tool calls in one request (CoderGamester batch_execute parity).",
        Tools = new[]
        {
            Tool("batch_execute", BatchId,
                "Run sequential tool calls. Pass calls as JSON array string: [{\"name\":\"...\",\"arguments\":{...}}, ...]. stopOnError default true. dryRun=true returns impact list without executing.",
                JsonSchemaHelper.Object(
                    ("callsJson", JsonSchemaHelper.String("JSON array of {name, arguments}"), true),
                    ("stopOnError", JsonSchemaHelper.Boolean("Stop on first error (default true)"), false),
                    ("dryRun", JsonSchemaHelper.Boolean("Preview planned calls without mutating (default false)"), false)
                ),
                async (args, ct) =>
                {
                    var json = Arg(args, "callsJson") ?? throw new ArgumentException("callsJson required");
                    var stopOnError = true;
                    if (args != null && args.TryGetPropertyValue("stopOnError", out var s) && s is JsonValue jv && jv.TryGetValue<bool>(out var b))
                        stopOnError = b;
                    var dryRun = false;
                    if (args != null && args.TryGetPropertyValue("dryRun", out var d) && d is JsonValue dv && dv.TryGetValue<bool>(out var db))
                        dryRun = db;

                    JsonArray? arr;
                    try { arr = JsonNode.Parse(json) as JsonArray; }
                    catch (Exception ex) { return ToolResult.Error("Invalid callsJson: " + ex.Message); }
                    if (arr == null) return ToolResult.Error("callsJson must be a JSON array");

                    if (dryRun)
                    {
                        var planned = new List<object>();
                        foreach (var item in arr)
                        {
                            if (item is not JsonObject call)
                            {
                                planned.Add(new { error = "invalid call entry" });
                                continue;
                            }
                            var name = call["name"]?.GetValue<string>();
                            planned.Add(new
                            {
                                name = name ?? "(missing)",
                                arguments = call["arguments"]?.ToJsonString() ?? "{}",
                                wouldExecute = !string.IsNullOrEmpty(name)
                            });
                        }
                        return ToolResult.OkJson(new { dryRun = true, count = planned.Count, wouldExecute = planned });
                    }

                    var results = new List<object>();
                    foreach (var item in arr)
                    {
                        if (item is not JsonObject call)
                        {
                            results.Add(new { error = "invalid call entry" });
                            if (stopOnError) break;
                            continue;
                        }
                        var name = call["name"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(name))
                        {
                            results.Add(new { error = "missing name" });
                            if (stopOnError) break;
                            continue;
                        }
                        var callArgs = call["arguments"] as JsonObject;
                        var r = await registry.CallAsync(name!, callArgs, ct).ConfigureAwait(false);
                        results.Add(new { name, isError = r.IsError, content = CompactResults.Truncate(r.Content, 2000) });
                        if (r.IsError && stopOnError) break;
                    }
                    return ToolResult.OkJson(new { count = results.Count, results });
                })
        }
    };

    private static ToolDefinition Tool(
        string name,
        string skillId,
        string description,
        JsonObject schema,
        Func<JsonObject?, CancellationToken, Task<ToolResult>> handler) =>
        new()
        {
            Name = name,
            SkillId = skillId,
            Description = description,
            InputSchema = schema,
            Handler = handler
        };

    private static string? Arg(JsonObject? args, string key)
    {
        if (args == null || !args.TryGetPropertyValue(key, out var n) || n is null) return null;
        return n.GetValue<string>();
    }

    private static int? IntArg(JsonObject? args, string key)
    {
        if (args == null || !args.TryGetPropertyValue(key, out var n) || n is not JsonValue jv)
            return null;
        if (jv.TryGetValue<int>(out var i)) return i;
        if (jv.TryGetValue<long>(out var l)) return (int)l;
        return null;
    }

    private static IReadOnlyList<string> SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsLive(IEditorHost editor) =>
        string.Equals(editor.HostMode, "live", StringComparison.OrdinalIgnoreCase);

    private static object WrapProfiler(IEditorHost editor)
    {
        var s = editor.GetProfilerSnapshot();
        return new
        {
            format = "json-metrics-snapshot",
            note = "Profiler.Get* memory/FPS counters only — not full Profiler window capture.",
            hostMode = editor.HostMode,
            enabled = s.Enabled,
            deltaTimeMs = s.DeltaTimeMs,
            fps = s.Fps,
            monoUsedBytes = s.MonoUsedBytes,
            totalAllocatedBytes = s.TotalAllocatedBytes,
            enabledModules = s.EnabledModules
        };
    }
}
