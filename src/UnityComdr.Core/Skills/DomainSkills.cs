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
            (TestingId, "Testing", "EditMode/PlayMode-style test run & list", "IvanMurzak tests-run / CoderGamester run_tests"),
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
        Description = "UPM package list/add/remove/search.",
        Tools = new[]
        {
            Tool("package_manage", PackagesId,
                "Action: list|add|remove|search.",
                JsonSchemaHelper.Object(
                    ("action", JsonSchemaHelper.String(null, new[] { "list", "add", "remove", "search" }), true),
                    ("package", JsonSchemaHelper.String("Package id, url, or name@version"), false),
                    ("query", JsonSchemaHelper.String("Search query"), false)
                ),
                async (args, _) =>
                {
                    var action = Arg(args, "action")?.ToLowerInvariant() ?? "list";
                    switch (action)
                    {
                        case "list":
                            return ToolResult.OkJson(new { packages = editor.ListPackages() });
                        case "add":
                        {
                            var pkg = Arg(args, "package") ?? throw new ArgumentException("package required");
                            return ToolResult.OkJson(editor.AddPackage(pkg));
                        }
                        case "remove":
                        {
                            var pkg = Arg(args, "package") ?? throw new ArgumentException("package required");
                            return editor.RemovePackage(pkg)
                                ? ToolResult.Ok($"Removed {pkg}")
                                : ToolResult.Error($"Package not found: {pkg}");
                        }
                        case "search":
                        {
                            var q = Arg(args, "query") ?? "";
                            return ToolResult.OkJson(new { results = editor.SearchPackages(q) });
                        }
                        default:
                            return ToolResult.Error($"Unknown action: {action}");
                    }
                })
        }
    };

    private static SkillDefinition BuildMenu(IEditorHost editor) => new()
    {
        Id = MenuId,
        Name = "Menu Items",
        Description = "List and execute Unity menu items (CoderGamester execute_menu_item).",
        Tools = new[]
        {
            Tool("menu_manage", MenuId,
                "Action: list|execute.",
                JsonSchemaHelper.Object(
                    ("action", JsonSchemaHelper.String(null, new[] { "list", "execute" }), true),
                    ("path", JsonSchemaHelper.String("Menu path e.g. GameObject/Create Empty"), false),
                    ("filter", JsonSchemaHelper.String("Filter menu list"), false)
                ),
                async (args, _) =>
                {
                    var action = Arg(args, "action")?.ToLowerInvariant() ?? "list";
                    if (action == "list")
                        return ToolResult.OkJson(new { items = editor.ListMenuItems(Arg(args, "filter")) });
                    if (action == "execute")
                    {
                        var path = Arg(args, "path") ?? throw new ArgumentException("path required");
                        return editor.ExecuteMenuItem(path)
                            ? ToolResult.OkJson(new { executed = path, state = editor.GetState() })
                            : ToolResult.Error($"Menu execute failed: {path}");
                    }
                    return ToolResult.Error($"Unknown action: {action}");
                })
        }
    };

    private static SkillDefinition BuildProfiling(IEditorHost editor) => new()
    {
        Id = ProfilingId,
        Name = "Profiling",
        Description = "Profiler start/stop/status/snapshot/save/load (IvanMurzak profiler suite).",
        Tools = new[]
        {
            Tool("profiler_manage", ProfilingId,
                "Action: start|stop|status|capture|clear|save|load.",
                JsonSchemaHelper.Object(
                    ("action", JsonSchemaHelper.String(null, new[]
                    {
                        "start", "stop", "status", "capture", "clear", "save", "load"
                    }), true),
                    ("path", JsonSchemaHelper.String("Snapshot path for save/load"), false)
                ),
                async (args, _) =>
                {
                    var action = Arg(args, "action")?.ToLowerInvariant() ?? "status";
                    switch (action)
                    {
                        case "start":
                            editor.SetProfilerEnabled(true);
                            return ToolResult.OkJson(editor.GetProfilerSnapshot());
                        case "stop":
                            editor.SetProfilerEnabled(false);
                            return ToolResult.OkJson(editor.GetProfilerSnapshot());
                        case "status":
                        case "capture":
                            return ToolResult.OkJson(editor.GetProfilerSnapshot());
                        case "clear":
                            editor.ClearProfilerData();
                            return ToolResult.Ok("Profiler data cleared.");
                        case "save":
                        {
                            var path = Arg(args, "path") ?? "Assets/Profiler/snapshot.json";
                            editor.SaveProfilerData(path);
                            return ToolResult.OkJson(new { saved = path });
                        }
                        case "load":
                        {
                            var path = Arg(args, "path") ?? throw new ArgumentException("path required");
                            var snap = editor.LoadProfilerData(path);
                            return snap == null
                                ? ToolResult.Error($"Snapshot not found: {path}")
                                : ToolResult.OkJson(snap);
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
        Description = "Capture camera / game view / scene view / isolated GO (IvanMurzak screenshot suite).",
        Tools = new[]
        {
            Tool("screenshot_capture", ScreenshotsId,
                "Capture a view. source: camera|game_view|scene_view|isolated.",
                JsonSchemaHelper.Object(
                    ("source", JsonSchemaHelper.String(null, new[] { "camera", "game_view", "scene_view", "isolated" }), true),
                    ("target", JsonSchemaHelper.String("Camera or GO id for camera/isolated"), false),
                    ("width", JsonSchemaHelper.Integer(), false),
                    ("height", JsonSchemaHelper.Integer(), false)
                ),
                async (args, _) =>
                {
                    var source = Arg(args, "source") ?? "game_view";
                    var w = 1280;
                    var h = 720;
                    if (args != null && args.TryGetPropertyValue("width", out var wn) && wn is JsonValue wv && wv.TryGetValue<int>(out var wi))
                        w = wi;
                    if (args != null && args.TryGetPropertyValue("height", out var hn) && hn is JsonValue hv && hv.TryGetValue<int>(out var hi))
                        h = hi;
                    var result = editor.CaptureScreenshot(source, Arg(args, "target"), w, h);
                    return ToolResult.OkJson(result);
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
                "Run sequential tool calls. Pass calls as JSON array string: [{\"name\":\"...\",\"arguments\":{...}}, ...]. stopOnError default true.",
                JsonSchemaHelper.Object(
                    ("callsJson", JsonSchemaHelper.String("JSON array of {name, arguments}"), true),
                    ("stopOnError", JsonSchemaHelper.Boolean("Stop on first error (default true)"), false)
                ),
                async (args, ct) =>
                {
                    var json = Arg(args, "callsJson") ?? throw new ArgumentException("callsJson required");
                    var stopOnError = true;
                    if (args != null && args.TryGetPropertyValue("stopOnError", out var s) && s is JsonValue jv && jv.TryGetValue<bool>(out var b))
                        stopOnError = b;

                    JsonArray? arr;
                    try { arr = JsonNode.Parse(json) as JsonArray; }
                    catch (Exception ex) { return ToolResult.Error("Invalid callsJson: " + ex.Message); }
                    if (arr == null) return ToolResult.Error("callsJson must be a JSON array");

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

    private static IReadOnlyList<string> SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
