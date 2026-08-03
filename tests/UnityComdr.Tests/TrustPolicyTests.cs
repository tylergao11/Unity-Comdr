using System.Text.Json;
using UnityComdr.Bootstrap;
using UnityComdr.Skills;
using UnityComdr.Trust;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>Phase T / FR-T1–T3: consent state machine, disable list, audit sink (Core, no Unity).</summary>
public class TrustPolicyTests
{
    [Fact]
    public void ConsentState_blocks_tools_until_approved_allows_doctor()
    {
        var consent = new ConsentState();
        Assert.Equal(ConsentDecision.Unknown, consent.Decision);
        Assert.False(consent.AllowsToolMethods);

        Assert.True(consent.TryAuthorize("ping", out var pingErr));
        Assert.Null(pingErr);
        Assert.True(consent.TryAuthorize("editor.getState", out _));
        Assert.True(consent.TryAuthorize("editor_state", out _));
        Assert.True(ConsentState.IsDoctorMethod("ping"));

        Assert.False(consent.TryAuthorize("go.create", out var err));
        Assert.Contains("consent_required", err!, StringComparison.OrdinalIgnoreCase);

        consent.MarkPending();
        Assert.False(consent.TryAuthorize("scene.open", out _));

        consent.Deny();
        Assert.False(consent.TryAuthorize("scene.open", out var denied));
        Assert.Contains("consent_denied", denied!, StringComparison.OrdinalIgnoreCase);

        consent.Approve();
        Assert.True(consent.AllowsToolMethods);
        Assert.True(consent.TryAuthorize("go.create", out var okErr));
        Assert.Null(okErr);

        consent.Revoke();
        Assert.False(consent.AllowsToolMethods);
        consent.RestoreFromPersisted(approved: true);
        Assert.True(consent.AllowsToolMethods);
    }

    [Fact]
    public void Disable_list_filters_tools_list_and_blocks_call()
    {
        var rt = new ComdrRuntime(trust: new TrustSettings());
        Assert.Contains(rt.Registry.GetActiveTools(), t => t.Name == "console_read");

        rt.Registry.SetDisabledTools(new[] { "console_read", "hierarchy_get" });
        var active = rt.Registry.GetActiveTools();
        Assert.DoesNotContain(active, t => t.Name.Equals("console_read", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(active, t => t.Name.Equals("hierarchy_get", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(active, t => t.Name == "editor_state");
    }

    [Fact]
    public async Task Disable_list_rejects_CallAsync_with_tool_disabled()
    {
        var rt = new ComdrRuntime(trust: new TrustSettings());
        rt.Registry.SetDisabledTools(new[] { "console_read" });

        var result = await rt.Registry.CallAsync("console_read", null);
        Assert.True(result.IsError);
        Assert.Contains("tool_disabled", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disabled_skill_not_loadable_and_tools_filtered()
    {
        var rt = new ComdrRuntime(trust: new TrustSettings());
        rt.Registry.SetDisabledSkills(new[] { SampleSkills.TestingSkillId });

        Assert.False(rt.Registry.LoadSkill(SampleSkills.TestingSkillId));
        Assert.DoesNotContain(rt.Registry.GetActiveTools(), t => t.Name == "tests_run");

        // Even if force-added to loaded set via re-enable then disable mid-session:
        rt.Registry.SetDisabledSkills(Array.Empty<string>());
        Assert.True(rt.Registry.LoadSkill(SampleSkills.TestingSkillId));
        Assert.Contains(rt.Registry.GetActiveTools(), t => t.Name == "tests_run");
        rt.Registry.SetDisabledSkills(new[] { SampleSkills.TestingSkillId });
        Assert.DoesNotContain(rt.Registry.GetActiveTools(), t => t.Name == "tests_run");
    }

    [Fact]
    public async Task Audit_sink_records_ok_and_error_calls()
    {
        var rt = new ComdrRuntime(trust: new TrustSettings());
        var sink = new MemoryAuditSink();
        rt.Registry.SetAuditSink(sink);

        var ok = await rt.Registry.CallAsync("editor_state", null);
        Assert.False(ok.IsError, ok.Content);

        var err = await rt.Registry.CallAsync("not_a_real_tool_xyz", null);
        Assert.True(err.IsError);

        var entries = sink.Entries;
        Assert.True(entries.Count >= 2);
        Assert.Contains(entries, e => e.ToolName == "editor_state" && e.Ok);
        Assert.Contains(entries, e => e.ToolName == "not_a_real_tool_xyz" && !e.Ok);
        Assert.All(entries, e => Assert.True(e.DurationMs >= 0));
        Assert.All(entries, e => Assert.NotEqual(default, e.Timestamp));
    }

    [Fact]
    public void TrustSettings_roundtrip_json_and_env_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "unity-comdr-trust-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settingsPath = Path.Combine(dir, "UnityComdr.mcp.json");
            var written = new TrustSettings();
            written.SetDisabledTools(new[] { "script_delete" });
            written.SetDisabledSkills(new[] { "batch" });
            written.AuditEnabled = true;
            File.WriteAllText(settingsPath, written.ToJson());

            var parsed = TrustSettings.FromJson(File.ReadAllText(settingsPath));
            Assert.True(parsed.IsToolDisabled("script_delete"));
            Assert.True(parsed.IsSkillDisabled("batch"));
            Assert.True(parsed.AuditEnabled);

            var prev = Environment.GetEnvironmentVariable(TrustSettings.TrustSettingsPathEnv);
            try
            {
                Environment.SetEnvironmentVariable(TrustSettings.TrustSettingsPathEnv, settingsPath);
                var loaded = TrustSettings.TryLoadFromEnvironment();
                Assert.NotNull(loaded);
                Assert.True(loaded!.IsToolDisabled("script_delete"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(TrustSettings.TrustSettingsPathEnv, prev);
            }

            var audit = TrustSettings.ResolveAuditLogPath(dir);
            Assert.EndsWith("unity-comdr-audit.jsonl", audit, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FileAuditSink_appends_jsonl_locally()
    {
        var dir = Path.Combine(Path.GetTempPath(), "unity-comdr-audit-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "unity-comdr-audit.jsonl");
        try
        {
            var sink = new FileAuditSink(path);
            sink.Append(new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                ToolName = "editor_state",
                Ok = true,
                DurationMs = 3
            });
            sink.Append(new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                ToolName = "console_read",
                Ok = false,
                DurationMs = 1,
                Error = "boom"
            });

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            using var doc = JsonDocument.Parse(lines[0]);
            Assert.Equal("editor_state", doc.RootElement.GetProperty("tool").GetString());
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(3, doc.RootElement.GetProperty("durationMs").GetInt64());
            Assert.Contains("\"ok\":false", lines[1], StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Mcp_tools_list_omits_disabled_tools()
    {
        var trust = new TrustSettings();
        trust.SetDisabledTools(new[] { "console_read" });
        var rt = new ComdrRuntime(trust: trust);
        var server = new UnityComdr.McpHost.McpServer(
            rt,
            new StringReader(""),
            new StringWriter());

        var response = await server.HandleLineAsync(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        Assert.NotNull(response);
        var text = response!.ToJsonString();
        Assert.DoesNotContain("\"name\":\"console_read\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"name\":\"editor_state\"", text, StringComparison.OrdinalIgnoreCase);
    }
}
