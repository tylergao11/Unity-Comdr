using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnityComdr.Tools;

public sealed class ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonObject InputSchema { get; init; }
    public required Func<JsonObject?, CancellationToken, Task<ToolResult>> Handler { get; init; }
    public string? SkillId { get; init; }
    public bool IsEscapeHatch { get; init; }
    public bool EnabledByDefault { get; init; } = true;
}

/// <summary>MCP image content payload (base64 bytes + mime type).</summary>
public sealed class ToolImageContent
{
    public string MimeType { get; init; } = "image/png";
    public string DataBase64 { get; init; } = "";
}

public sealed class ToolResult
{
    public bool IsError { get; init; }
    public string Content { get; init; } = "";
    public object? Structured { get; init; }
    public IReadOnlyList<ToolImageContent>? Images { get; init; }
    /// <summary>True when <see cref="Content"/> is already an O3 ok/error envelope.</summary>
    public bool IsEnvelope { get; init; }

    public static ToolResult Ok(string content, object? structured = null) =>
        new() { Content = content, Structured = structured, IsError = false };

    public static ToolResult OkJson(object structured) =>
        new()
        {
            Content = JsonSerializer.Serialize(structured, Util.CompactResults.JsonOptions),
            Structured = structured,
            IsError = false
        };

    /// <summary>O3 success envelope: <c>{ ok:true, data, hint? }</c>.</summary>
    public static ToolResult OkEnvelope(object data, string? hint = null) =>
        FromEnvelope(ok: true, data: data, hint: hint, images: null);

    /// <summary>
    /// Success with one or more MCP image content blocks.
    /// <paramref name="content"/> should be short metadata only — never embed PNG base64 in text.
    /// </summary>
    public static ToolResult OkWithImages(
        string content,
        IEnumerable<ToolImageContent> images,
        object? structured = null) =>
        new()
        {
            Content = content,
            Structured = structured,
            Images = images as IReadOnlyList<ToolImageContent> ?? images.ToList(),
            IsError = false
        };

    public static ToolResult Error(string message) =>
        new() { Content = message, IsError = true };

    /// <summary>O3 error envelope: <c>{ ok:false, error:{ code, message, suggestion?, nextStep? } }</c>.</summary>
    public static ToolResult ErrorEnvelope(
        string code,
        string message,
        string? suggestion = null,
        string? nextStep = null)
    {
        var error = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = code,
            ["message"] = message
        };
        if (!string.IsNullOrEmpty(suggestion)) error["suggestion"] = suggestion;
        if (!string.IsNullOrEmpty(nextStep)) error["nextStep"] = nextStep;
        var envelope = new { ok = false, error };
        return new()
        {
            Content = JsonSerializer.Serialize(envelope, Util.CompactResults.JsonOptions),
            Structured = envelope,
            IsError = true,
            IsEnvelope = true
        };
    }

    internal static ToolResult FromEnvelope(
        bool ok,
        object? data,
        string? hint,
        IReadOnlyList<ToolImageContent>? images)
    {
        object envelope = string.IsNullOrEmpty(hint)
            ? new { ok, data }
            : new { ok, data, hint };
        return new()
        {
            Content = JsonSerializer.Serialize(envelope, Util.CompactResults.JsonOptions),
            Structured = envelope,
            Images = images,
            IsError = !ok,
            IsEnvelope = true
        };
    }
}

public sealed class SkillDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<ToolDefinition> Tools { get; init; }
}

/// <summary>Hard budget for default session tool schemas (token frugality).</summary>
public static class ToolBudget
{
    /// <summary>P0 core (15) + P1 ui_query / input_simulate / lease_acquire / lease_release.</summary>
    public const int MaxDefaultCoreTools = 19;
}
