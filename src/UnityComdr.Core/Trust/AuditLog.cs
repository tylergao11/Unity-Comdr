using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityComdr.Trust;

/// <summary>FR-T3 local invocation audit record (no phone-home).</summary>
public sealed class AuditEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string ToolName { get; init; }
    public bool Ok { get; init; }
    public long DurationMs { get; init; }
    public string? Error { get; init; }
}

/// <summary>Append-only audit sink for tool invocations.</summary>
public interface IAuditSink
{
    void Append(AuditEntry entry);
}

/// <summary>In-memory sink for tests.</summary>
public sealed class MemoryAuditSink : IAuditSink
{
    private readonly List<AuditEntry> _entries = new();
    private readonly object _gate = new();

    public IReadOnlyList<AuditEntry> Entries
    {
        get
        {
            lock (_gate) return _entries.ToList();
        }
    }

    public void Append(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate) _entries.Add(entry);
    }
}

/// <summary>Append-only JSONL file under project Temp/Logs (local only).</summary>
public sealed class FileAuditSink : IAuditSink
{
    private readonly string _path;
    private readonly object _gate = new();

    public string Path => _path;

    public FileAuditSink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public void Append(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var line = JsonSerializer.Serialize(new
        {
            timestamp = entry.Timestamp.UtcDateTime.ToString("o"),
            tool = entry.ToolName,
            ok = entry.Ok,
            durationMs = entry.DurationMs,
            error = entry.Error
        }, JsonOpts);
        lock (_gate)
            File.AppendAllText(_path, line + Environment.NewLine);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Helpers for timing tool calls into an optional sink.</summary>
public static class AuditLog
{
    public static async Task<T> MeasureAsync<T>(
        IAuditSink? sink,
        string toolName,
        Func<Task<T>> action,
        Func<T, bool> isOk,
        Func<T, string?> errorText)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action().ConfigureAwait(false);
            sw.Stop();
            sink?.Append(new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                ToolName = toolName,
                Ok = isOk(result),
                DurationMs = sw.ElapsedMilliseconds,
                Error = isOk(result) ? null : errorText(result)
            });
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            sink?.Append(new AuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                ToolName = toolName,
                Ok = false,
                DurationMs = sw.ElapsedMilliseconds,
                Error = ex.Message
            });
            throw;
        }
    }
}
