using System.Text.Json.Nodes;
using UnityComdr.Editor;
using UnityComdr.Models;
using UnityComdr.Tools;
using UnityComdr.Util;

namespace UnityComdr.Skills;

/// <summary>
/// Sample domain skills. Testing uses live TestRunnerApi via IEditorHost — never fakes console/script counts as tests.
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
                Description =
                    "Start Unity Test Runner job (live hostMode only, TestRunnerApi). Returns {jobId,status}. Poll tests_status. mode: EditMode|PlayMode. Headless returns isError (no fake results).",
                SkillId = TestingSkillId,
                InputSchema = JsonSchemaHelper.Object(
                    ("filter", JsonSchemaHelper.String("Optional name filter"), false),
                    ("mode", JsonSchemaHelper.String("EditMode or PlayMode", new[] { "EditMode", "PlayMode" }), false)
                ),
                Handler = async (args, _) =>
                {
                    if (!IsLive(editor))
                        return await Task.FromResult(ToolResult.ErrorEnvelope(
                            "requires_live",
                            "tests_run requires hostMode=live (Unity TestRunnerApi). Headless does not simulate tests.",
                            nextStep: "Open Unity with Unity-Comdr bridge, confirm editor_state.hostMode=live, then retry."));

                    var mode = args != null && args.TryGetPropertyValue("mode", out var m) && m != null
                        ? m.GetValue<string>()
                        : "EditMode";
                    var filter = args != null && args.TryGetPropertyValue("filter", out var f) && f != null
                        ? f.GetValue<string>()
                        : null;

                    TestJobSnapshot job;
                    try
                    {
                        job = editor.StartTests(mode ?? "EditMode", filter);
                    }
                    catch (Exception ex)
                    {
                        return ToolResult.ErrorEnvelope(
                            "testrunner_error",
                            ex.Message,
                            nextStep: "Ensure com.unity.test-framework is installed and Test Runner window works.");
                    }

                    if (string.Equals(job.Status, "unsupported", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(job.Status, "failed", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(job.JobId))
                    {
                        return ToolResult.ErrorEnvelope(
                            "testrunner_unavailable",
                            job.Note ?? "TestRunnerApi unavailable on this Editor.",
                            nextStep: "Install Test Framework package or run tests manually in Unity.");
                    }

                    return await Task.FromResult(ToolResult.OkJson(new
                    {
                        jobId = job.JobId,
                        status = job.Status,
                        mode = job.Mode,
                        hostMode = editor.HostMode,
                        note = job.Note
                    }));
                }
            },
            new()
            {
                Name = "tests_status",
                Description = "Poll a test job started by tests_run (live TestRunnerApi). Pass jobId.",
                SkillId = TestingSkillId,
                InputSchema = JsonSchemaHelper.Object(
                    ("jobId", JsonSchemaHelper.String("Job id from tests_run"), true)
                ),
                Handler = async (args, _) =>
                {
                    if (!IsLive(editor))
                        return await Task.FromResult(ToolResult.ErrorEnvelope(
                            "requires_live",
                            "tests_status requires hostMode=live.",
                            nextStep: "Use live Unity bridge."));

                    var jobId = args?["jobId"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(jobId))
                        return ToolResult.ErrorEnvelope(
                            "bad_argument",
                            "Missing required parameter 'jobId'.",
                            nextStep: "Pass jobId from the tests_run response.");

                    try
                    {
                        var job = editor.GetTestJob(jobId!);
                        if (string.Equals(job.Status, "unsupported", StringComparison.OrdinalIgnoreCase))
                            return ToolResult.ErrorEnvelope(
                                "testrunner_unavailable",
                                job.Note ?? "unsupported",
                                nextStep: "Start a job with tests_run on live Editor.");

                        return await Task.FromResult(ToolResult.OkJson(new
                        {
                            jobId = job.JobId,
                            status = job.Status,
                            mode = job.Mode,
                            filter = job.Filter,
                            passed = job.Passed,
                            results = job.Results,
                            note = job.Note,
                            hostMode = editor.HostMode
                        }));
                    }
                    catch (Exception ex)
                    {
                        return ToolResult.ErrorEnvelope(
                            "testrunner_error",
                            ex.Message,
                            nextStep: "Check jobId from tests_run / tests_list and Unity Test Runner.");
                    }
                }
            },
            new()
            {
                Name = "tests_list",
                Description = "List tests discovered by Unity TestRunnerApi (live only). Empty/error on headless.",
                SkillId = TestingSkillId,
                InputSchema = JsonSchemaHelper.Object(
                    ("mode", JsonSchemaHelper.String("EditMode or PlayMode", new[] { "EditMode", "PlayMode" }), false)
                ),
                Handler = async (args, _) =>
                {
                    if (!IsLive(editor))
                        return await Task.FromResult(ToolResult.ErrorEnvelope(
                            "requires_live",
                            "tests_list requires hostMode=live TestRunnerApi (no hardcoded fake catalog).",
                            nextStep: "Open Unity with bridge connected."));

                    var mode = args != null && args.TryGetPropertyValue("mode", out var m) && m != null
                        ? m.GetValue<string>()
                        : null;
                    try
                    {
                        var tests = editor.ListTests(mode);
                        return await Task.FromResult(ToolResult.OkJson(new
                        {
                            hostMode = editor.HostMode,
                            tests
                        }));
                    }
                    catch (Exception ex)
                    {
                        return ToolResult.ErrorEnvelope("testrunner_error", ex.Message,
                            nextStep: "Check Test Framework package.");
                    }
                }
            }
        };

        return new SkillDefinition
        {
            Id = TestingSkillId,
            Name = "Testing",
            Description = "Unity Test Runner (TestRunnerApi) job start/poll/list — live Editor only.",
            Tools = tools
        };
    }

    private static bool IsLive(IEditorHost editor) =>
        string.Equals(editor.HostMode, "live", StringComparison.OrdinalIgnoreCase);

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
