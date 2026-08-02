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

public sealed class ToolResult
{
    public bool IsError { get; init; }
    public string Content { get; init; } = "";
    public object? Structured { get; init; }

    public static ToolResult Ok(string content, object? structured = null) =>
        new() { Content = content, Structured = structured, IsError = false };

    public static ToolResult OkJson(object structured) =>
        new()
        {
            Content = JsonSerializer.Serialize(structured, Util.CompactResults.JsonOptions),
            Structured = structured,
            IsError = false
        };

    public static ToolResult Error(string message) =>
        new() { Content = message, IsError = true };
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
