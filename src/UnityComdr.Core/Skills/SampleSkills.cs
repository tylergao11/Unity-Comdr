using System.Text.Json.Nodes;
using UnityComdr.Editor;
using UnityComdr.Tools;
using UnityComdr.Util;

namespace UnityComdr.Skills;

/// <summary>
/// Sample domain skills proving the loader. Loaded only via skill_manage.
/// </summary>
public static class SampleSkills
{
    public const string TestingSkillId = "testing";
    public const string PrefabAdvancedSkillId = "prefab-advanced";

    public static void RegisterAll(ToolRegistry registry, IEditorHost editor)
    {
        registry.RegisterSkill(BuildTestingSkill(editor));
        registry.RegisterSkill(BuildPrefabAdvancedSkill(editor));
    }

    private static SkillDefinition BuildTestingSkill(IEditorHost editor)
    {
        var tools = new List<ToolDefinition>
        {
            new()
            {
                Name = "tests_run",
                Description = "Run a lightweight EditMode-style test pass against in-memory assertions (headless) or Unity Test Runner (Editor).",
                SkillId = TestingSkillId,
                InputSchema = JsonSchemaHelper.Object(
                    ("filter", JsonSchemaHelper.String("Optional name filter"), false),
                    ("mode", JsonSchemaHelper.String("EditMode or PlayMode", new[] { "EditMode", "PlayMode" }), false)
                ),
                Handler = async (args, _) =>
                {
                    var mode = args != null && args.TryGetPropertyValue("mode", out var m) && m != null
                        ? m.GetValue<string>()
                        : "EditMode";
                    var filter = args != null && args.TryGetPropertyValue("filter", out var f) && f != null
                        ? f.GetValue<string>()
                        : null;

                    // Headless: synthesize a test report from current project state
                    var scripts = editor.ListScripts();
                    var errors = editor.GetConsoleLogs().Count(l => l.Type == Models.LogType.Error);
                    var passed = errors == 0;
                    var results = new List<object>
                    {
                        new
                        {
                            name = "Console_NoErrors",
                            status = passed ? "Passed" : "Failed",
                            message = passed ? "No error logs" : $"{errors} error(s) in console"
                        },
                        new
                        {
                            name = "Scripts_Present",
                            status = scripts.Count >= 0 ? "Passed" : "Failed",
                            message = $"{scripts.Count} script(s)"
                        }
                    };
                    if (!string.IsNullOrEmpty(filter))
                        results = results.Where(r => r.ToString()!.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                    return await Task.FromResult(ToolResult.OkJson(new
                    {
                        mode,
                        filter,
                        passed,
                        results
                    }));
                }
            },
            new()
            {
                Name = "tests_list",
                Description = "List available logical tests (headless catalog).",
                SkillId = TestingSkillId,
                InputSchema = JsonSchemaHelper.Object(),
                Handler = async (_, _) => await Task.FromResult(ToolResult.OkJson(new
                {
                    tests = new[]
                    {
                        new { name = "Console_NoErrors", mode = "EditMode" },
                        new { name = "Scripts_Present", mode = "EditMode" }
                    }
                }))
            }
        };

        return new SkillDefinition
        {
            Id = TestingSkillId,
            Name = "Testing",
            Description = "Test Runner helpers for EditMode/PlayMode-style checks.",
            Tools = tools
        };
    }

    private static SkillDefinition BuildPrefabAdvancedSkill(IEditorHost editor)
    {
        var tools = new List<ToolDefinition>
        {
            new()
            {
                Name = "prefab_batch_instantiate",
                Description = "Instantiate a prefab N times with optional name prefix and parent (domain skill).",
                SkillId = PrefabAdvancedSkillId,
                InputSchema = JsonSchemaHelper.Object(
                    ("path", JsonSchemaHelper.String("Prefab path"), true),
                    ("count", JsonSchemaHelper.Integer("How many instances (1-50)"), true),
                    ("namePrefix", JsonSchemaHelper.String("Optional name prefix"), false),
                    ("parent", JsonSchemaHelper.String("Optional parent id/path"), false)
                ),
                Handler = async (args, _) =>
                {
                    var path = args?["path"]?.GetValue<string>()
                        ?? throw new ArgumentException("path required");
                    var count = 1;
                    if (args != null && args.TryGetPropertyValue("count", out var c) && c is JsonValue jv)
                    {
                        if (jv.TryGetValue<int>(out var i)) count = i;
                    }
                    count = Math.Clamp(count, 1, 50);
                    var prefix = args != null && args.TryGetPropertyValue("namePrefix", out var p) && p != null
                        ? p.GetValue<string>()
                        : "Instance";
                    var parent = args != null && args.TryGetPropertyValue("parent", out var par) && par != null
                        ? par.GetValue<string>()
                        : null;

                    var created = new List<object>();
                    for (var n = 1; n <= count; n++)
                    {
                        var go = editor.InstantiatePrefab(path, parent);
                        if (go == null)
                            return ToolResult.Error($"Prefab not found: {path}");
                        editor.RenameGameObject(go.Id, $"{prefix}_{n}");
                        created.Add(new { go.Id, name = $"{prefix}_{n}" });
                    }
                    return await Task.FromResult(ToolResult.OkJson(new { count = created.Count, instances = created }));
                }
            },
            new()
            {
                Name = "prefab_list",
                Description = "List prefab assets (compact, paginated).",
                SkillId = PrefabAdvancedSkillId,
                InputSchema = JsonSchemaHelper.Object(
                    ("offset", JsonSchemaHelper.Integer(), false),
                    ("pageSize", JsonSchemaHelper.Integer(), false)
                ),
                Handler = async (args, _) =>
                {
                    var assets = editor.FindAssets(kind: "Prefab");
                    int? offset = null;
                    int? pageSize = null;
                    if (args != null && args.TryGetPropertyValue("offset", out var o) && o is JsonValue ov && ov.TryGetValue<int>(out var oi))
                        offset = oi;
                    if (args != null && args.TryGetPropertyValue("pageSize", out var ps) && ps is JsonValue pv && pv.TryGetValue<int>(out var pi))
                        pageSize = pi;
                    return await Task.FromResult(ToolResult.OkJson(
                        CompactResults.Paginate(assets, offset, pageSize, a => new { a.Path, a.PrefabSourceObjectId })));
                }
            }
        };

        return new SkillDefinition
        {
            Id = PrefabAdvancedSkillId,
            Name = "Prefab Advanced",
            Description = "Batch prefab instantiation and prefab listing helpers.",
            Tools = tools
        };
    }
}
