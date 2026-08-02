using System.Text.RegularExpressions;
using UnityComdr.Editor;
using UnityComdr.Util;
using Xunit;

namespace UnityComdr.Tests;

/// <summary>
/// Launch-safety properties for live bridge JSON (script_write multi-line + \uXXXX).
/// Core BridgeJson is the tested algorithm; LiveUnityBridgeServer.ExtractString must stay in parity.
/// </summary>
public class BridgeJsonTests
{
    [Fact]
    public void ExtractString_unescapes_newlines_for_script_write()
    {
        var line = "{\"method\":\"script.write\",\"path\":\"Assets/Scripts/Broken.cs\",\"content\":" +
                   "\"using UnityEngine;\\npublic class Broken : MonoBehaviour { public int value = 1; }\\n\"}";
        var content = BridgeJson.ExtractString(line, "content");
        Assert.NotNull(content);
        Assert.Contains("\n", content);
        Assert.Contains("class Broken", content);
        var lines = content!.Replace("\r", "").Split('\n', StringSplitOptions.None);
        Assert.True(lines.Length >= 2, $"expected multi-line, got {lines.Length}: {content}");
        Assert.Equal("using UnityEngine;", lines[0]);
        Assert.StartsWith("public class Broken", lines[1]);
    }

    [Fact]
    public void ExtractString_unescapes_unicode_non_ascii_script_comments()
    {
        // System.Text.Json often emits non-ASCII as \uXXXX — live script.write must not corrupt.
        // "中" = \u4e2d, "文" = \u6587, "注" = \u6ce8, "释" = \u91ca
        var line =
            "{\"content\":\"// \\u4e2d\\u6587\\u6ce8\\u91ca\\npublic class C {}\"}";
        var content = BridgeJson.ExtractString(line, "content");
        Assert.NotNull(content);
        Assert.Equal("// 中文注释\npublic class C {}", content);
        Assert.DoesNotContain("4e2d", content);
        Assert.DoesNotContain("87注", content); // corruption pattern from over-skip
        Assert.DoesNotContain("注CA", content);
    }

    [Fact]
    public void ExtractString_unescapes_quotes_and_unicode_bang()
    {
        var line = "{\"msg\":\"say \\\"hi\\\"\\u0021\"}";
        var msg = BridgeJson.ExtractString(line, "msg");
        Assert.Equal("say \"hi\"!", msg);
    }

    [Fact]
    public void ExtractStringArray_parses_bridge_client_selection_wire_format()
    {
        // Matches BridgeClientEditorHost serialization of SetSelection
        var line =
            "{\"id\":\"1\",\"method\":\"selection.set\",\"args\":{" +
            "\"gameObjectIds\":[\"abc123\",\"def456\"]," +
            "\"assetPaths\":[\"Assets/Foo.mat\",\"Assets/Bar.prefab\"]}}";
        var gos = BridgeJson.ExtractStringArray(line, "gameObjectIds");
        Assert.Equal(new[] { "abc123", "def456" }, gos);
        var assets = BridgeJson.ExtractStringArray(line, "assetPaths");
        Assert.Equal(new[] { "Assets/Foo.mat", "Assets/Bar.prefab" }, assets);
    }

    [Fact]
    public void ExtractString_roundtrip_quote_and_extract_preserves_chinese()
    {
        const string original = "using UnityEngine;\n// 中文注释\npublic class A : MonoBehaviour {}\n";
        var quoted = BridgeJson.Quote(original);
        var line = "{\"content\":" + quoted + "}";
        var back = BridgeJson.ExtractString(line, "content");
        Assert.Equal(original, back);
    }

    [Fact]
    public void Hierarchy_with_real_root_and_child_ids_includes_names()
    {
        var parentId = "root1";
        var childId = "child1";
        var all = new List<Models.GameObjectData>
        {
            new()
            {
                Id = parentId,
                Name = "Player",
                ChildIds = { childId },
                Components = { new Models.ComponentData { TypeName = "Transform" } }
            },
            new()
            {
                Id = childId,
                Name = "Weapon",
                ParentId = parentId,
                Components = { new Models.ComponentData { TypeName = "Transform" } }
            }
        };
        var summary = CompactResults.HierarchySummary(all, new[] { parentId }, maxDepth: 3, maxNodes: 40);
        var json = CompactResults.ToJson(summary);
        Assert.Contains("Player", json);
        Assert.Contains("Weapon", json);
        Assert.DoesNotContain("\"nodeCount\":0", json.Replace(" ", ""));
    }

    [Fact]
    public void Live_server_ExtractString_matches_BridgeJson_unescape_rules()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var livePath = Path.Combine(repoRoot, "packages", "com.unitycomdr.mcp", "Editor", "LiveUnityBridgeServer.cs");
        var corePath = Path.Combine(repoRoot, "src", "UnityComdr.Core", "Editor", "BridgeJson.cs");
        Assert.True(File.Exists(livePath), livePath);
        Assert.True(File.Exists(corePath), corePath);

        var liveSrc = File.ReadAllText(livePath);
        var coreSrc = File.ReadAllText(corePath);

        // Live unicode branch: only i+=5 (forbid assignment i+=4 as statement — the over-skip bug).
        Assert.DoesNotMatch(new Regex(@"\bi\s*\+=\s*4\b"), liveSrc);
        Assert.Contains("i += 5", liveSrc);
        Assert.Contains("case 'u':", liveSrc);
        Assert.Contains("case 'n':", liveSrc);
        Assert.Contains("sb.Append('\\n')", liveSrc);
        Assert.Contains("GetRootGameObjects", liveSrc);
        Assert.Contains("childCount", liveSrc);
        Assert.Contains("componentType", liveSrc);

        var liveBody = ExtractMethodBody(liveSrc, "ExtractString");
        var coreBody = ExtractMethodBody(coreSrc, "ExtractString");
        Assert.NotNull(liveBody);
        Assert.NotNull(coreBody);
        var liveU = Normalize(liveBody!);
        var coreU = Normalize(coreBody!);
        Assert.Contains("case'u':", liveU);
        Assert.Contains("i+=5", liveU);
        Assert.Contains("continue", liveU);
        Assert.DoesNotMatch(new Regex(@"i\+=4"), liveU);
        Assert.Contains("i+=5", coreU);
        Assert.Contains("case'u':", coreU);

        // Ship-path parity: evaluate the SAME payloads BridgeJson handles, and require
        // live source still implements the fixed rules (no re-introduction of double advance).
        // Directly re-run algorithm via BridgeJson (canonical) — live is locked by fingerprint above.
        var payloads = new[]
        {
            ("// \\u4e2d\\u6587\\u6ce8\\u91ca\\npublic class C {}", "// 中文注释\npublic class C {}"),
            ("line1\\nline2\\n", "line1\nline2\n"),
            ("a\\tb\\r\\nc", "a\tb\r\nc"),
        };
        foreach (var (encoded, expected) in payloads)
        {
            var line = "{\"content\":\"" + encoded + "\"}";
            Assert.Equal(expected, BridgeJson.ExtractString(line, "content"));
        }
    }

    private static string? ExtractMethodBody(string src, string methodName)
    {
        var m = Regex.Match(src,
            @"static\s+string\??\s+" + Regex.Escape(methodName) + @"\s*\([^)]*\)\s*\{",
            RegexOptions.Multiline);
        if (!m.Success) return null;
        var start = m.Index + m.Length - 1; // at '{'
        var depth = 0;
        for (var i = start; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return src.Substring(start, i - start + 1);
            }
        }
        return null;
    }

    private static string Normalize(string s) =>
        Regex.Replace(s, @"\s+", "").Replace("\r", "").Replace("\n", "");
}
