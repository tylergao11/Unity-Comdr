using System.Text.Json;
using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.Editor;
using UnityComdr.Skills;
using UnityComdr.Tools;
using UnityComdr.Util;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// Phase E / O3–O5 / A4–A5 / A9: agent-friendly envelopes, mutation echo, dryRun, pagination digDeeper, test jobs.
/// </summary>
public class AgentUxEnvelopeTests
{
    [Fact]
    public void OkEnvelope_and_ErrorEnvelope_have_stable_shape()
    {
        var ok = ToolResult.OkEnvelope(new { path = "Assets/A.cs", length = 3 }, hint: "re-read if needed");
        Assert.False(ok.IsError);
        Assert.True(ok.IsEnvelope);
        using (var doc = JsonDocument.Parse(ok.Content))
        {
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("Assets/A.cs", doc.RootElement.GetProperty("data").GetProperty("path").GetString());
            Assert.Equal("re-read if needed", doc.RootElement.GetProperty("hint").GetString());
        }

        var err = ToolResult.ErrorEnvelope(
            "not_found",
            "Not found: Missing",
            suggestion: "Did you mean 'Cube'?",
            nextStep: "Retry with target='Cube'");
        Assert.True(err.IsError);
        Assert.True(err.IsEnvelope);
        using (var doc = JsonDocument.Parse(err.Content))
        {
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            var error = doc.RootElement.GetProperty("error");
            Assert.Equal("not_found", error.GetProperty("code").GetString());
            Assert.Equal("Not found: Missing", error.GetProperty("message").GetString());
            Assert.Equal("Did you mean 'Cube'?", error.GetProperty("suggestion").GetString());
            Assert.Equal("Retry with target='Cube'", error.GetProperty("nextStep").GetString());
        }
    }

    [Fact]
    public async Task Registry_wraps_success_and_error_as_envelopes()
    {
        var rt = new ComdrRuntime(new InMemoryEditorHost());
        var ok = await rt.Registry.CallAsync("editor_state", null);
        Assert.False(ok.IsError, ok.Content);
        Assert.Contains("\"ok\":true", ok.Content.Replace(" ", ""));
        Assert.Contains("\"data\":", ok.Content);

        var err = await rt.Registry.CallAsync("tests_run", Obj(("mode", "EditMode")));
        Assert.True(err.IsError);
        Assert.Contains("\"ok\":false", err.Content.Replace(" ", ""));
        Assert.Contains("\"nextStep\"", err.Content);
        Assert.Contains("skill_manage", err.Content);
        Assert.Contains("action=load", err.Content);
    }

    [Fact]
    public async Task Mutation_echo_returns_post_state_summary()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);

        var created = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "create"), ("name", "EchoCube"), ("primitive", "Cube")));
        Assert.False(created.IsError, created.Content);
        Assert.Contains("\"name\":\"EchoCube\"", created.Content.Replace(" ", ""));
        Assert.Contains("\"transform\":", created.Content);
        Assert.Contains("\"active\":", created.Content);

        var renamed = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "rename"), ("target", "EchoCube"), ("name", "EchoRenamed")));
        Assert.False(renamed.IsError, renamed.Content);
        Assert.Contains("EchoRenamed", renamed.Content);
        Assert.Contains("\"id\":", renamed.Content);

        var moved = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "set_transform"),
            ("target", "EchoRenamed"),
            ("position", new JsonObject { ["x"] = 1, ["y"] = 2, ["z"] = 3 })));
        Assert.False(moved.IsError, moved.Content);
        Assert.Contains("\"x\":1", moved.Content.Replace(" ", ""));

        var write = await rt.Registry.CallAsync("script_write", Obj(
            ("path", "Assets/Scripts/Echo.cs"),
            ("content", "public class Echo {}")));
        Assert.False(write.IsError, write.Content);
        Assert.Contains("\"path\":", write.Content);
        Assert.Contains("\"length\":", write.Content);

        var add = await rt.Registry.CallAsync("component_manage", Obj(
            ("action", "add"), ("target", "EchoRenamed"), ("type", "Rigidbody")));
        Assert.False(add.IsError, add.Content);
        Assert.Contains("Rigidbody", add.Content);
        Assert.Contains("component", add.Content);
    }

    [Fact]
    public async Task DryRun_delete_and_batch_do_not_mutate()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);
        await rt.Registry.CallAsync("gameobject_manage", Obj(("action", "create"), ("name", "KeepMe")));
        await rt.Registry.CallAsync("script_write", Obj(
            ("path", "Assets/Scripts/Keep.cs"), ("content", "public class Keep {}")));
        await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "material_create"), ("path", "Assets/Mats/Keep.mat")));

        var goDry = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "delete"), ("target", "KeepMe"), ("dryRun", true)));
        Assert.False(goDry.IsError, goDry.Content);
        Assert.Contains("\"dryRun\":true", goDry.Content.Replace(" ", ""));
        Assert.Contains("wouldDelete", goDry.Content);
        Assert.NotNull(host.FindGameObject("KeepMe"));

        var scriptDry = await rt.Registry.CallAsync("script_delete", Obj(
            ("path", "Assets/Scripts/Keep.cs"), ("dryRun", true)));
        Assert.False(scriptDry.IsError, scriptDry.Content);
        Assert.Contains("wouldDelete", scriptDry.Content);
        Assert.NotNull(host.ReadScript("Assets/Scripts/Keep.cs"));

        var assetDry = await rt.Registry.CallAsync("assets_manage", Obj(
            ("action", "delete"), ("path", "Assets/Mats/Keep.mat"), ("dryRun", true)));
        Assert.False(assetDry.IsError, assetDry.Content);
        Assert.Contains("wouldDelete", assetDry.Content);
        Assert.Contains(host.FindAssets(kind: "Material"), a => a.Path.Contains("Keep.mat"));

        Assert.True(rt.Registry.LoadSkill(DomainSkills.BatchId));
        host.AddConsoleLog(new Models.ConsoleLogEntry(Models.LogType.Log, "still-here"));
        var batchDry = await rt.Registry.CallAsync("batch_execute", Obj(
            ("dryRun", true),
            ("callsJson", "[{\"name\":\"console_clear\",\"arguments\":{}}]")));
        Assert.False(batchDry.IsError, batchDry.Content);
        Assert.Contains("wouldExecute", batchDry.Content);
        // dryRun must not have cleared console
        Assert.Contains(host.GetConsoleLogs(), l => l.Message == "still-here");
    }

    [Fact]
    public async Task Unknown_tool_error_includes_nextStep()
    {
        var rt = new ComdrRuntime();
        var denied = await rt.Registry.CallAsync("tests_run", Obj(("mode", "EditMode")));
        Assert.True(denied.IsError);
        using var doc = JsonDocument.Parse(denied.Content);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        var error = doc.RootElement.GetProperty("error");
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("nextStep").GetString()));
        Assert.Contains("skill_manage", denied.Content);
    }

    [Fact]
    public async Task Not_found_gameobject_suggests_nearest_name()
    {
        var host = new InMemoryEditorHost();
        host.CreateGameObject("PlayerController");
        var rt = new ComdrRuntime(host);

        var miss = await rt.Registry.CallAsync("gameobject_manage", Obj(
            ("action", "get"), ("target", "PlayerControllr")));
        Assert.True(miss.IsError);
        Assert.Contains("PlayerController", miss.Content);
        Assert.Contains("nextStep", miss.Content);
        Assert.Contains("Did you mean", miss.Content);
    }

    [Fact]
    public void Paginate_truncated_page_includes_hasMore_and_digDeeper()
    {
        var items = Enumerable.Range(0, 25).Select(i => $"item-{i}").ToList();
        var page = CompactResults.Paginate(items, offset: 0, pageSize: 10);
        var json = CompactResults.ToJson(page);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(25, doc.RootElement.GetProperty("total").GetInt32());
        Assert.True(doc.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal(
            "Pass offset=10 pageSize=10 to continue.",
            doc.RootElement.GetProperty("digDeeper").GetString());
    }

    [Fact]
    public async Task Tests_run_returns_job_and_tests_status_polls()
    {
        var host = new InMemoryEditorHost();
        var rt = new ComdrRuntime(host);
        Assert.True(rt.Registry.LoadSkill(SampleSkills.TestingSkillId));

        var started = await rt.Registry.CallAsync("tests_run", Obj(("mode", "EditMode")));
        Assert.False(started.IsError, started.Content);
        Assert.Contains("jobId", started.Content);
        Assert.Contains("status", started.Content);

        using var startDoc = JsonDocument.Parse(started.Content);
        var jobId = startDoc.RootElement.GetProperty("data").GetProperty("jobId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        var status = await rt.Registry.CallAsync("tests_status", Obj(("jobId", jobId!)));
        Assert.False(status.IsError, status.Content);
        Assert.Contains("results", status.Content);
        Assert.Contains("Console_NoErrors", status.Content);
        Assert.Contains("\"status\":\"completed\"", status.Content.Replace(" ", ""));
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
