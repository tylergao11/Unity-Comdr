using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityComdr.Bootstrap;
using UnityComdr.Tools;
using UnityComdr.Util;

namespace UnityComdr.McpHost;

/// <summary>
/// Minimal MCP (Model Context Protocol) JSON-RPC server over newline-delimited stdio.
/// Supports initialize, tools/list, tools/call, ping, notifications/initialized.
/// </summary>
public sealed class McpServer
{
    private readonly ComdrRuntime _runtime;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter? _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public McpServer(ComdrRuntime runtime, TextReader input, TextWriter output, TextWriter? log = null)
    {
        _runtime = runtime;
        _input = input;
        _output = output;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _input.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                await HandleLineAsync(line, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.WriteLine($"handler error: {ex}");
                // If we cannot parse id, best-effort error without id
                await WriteAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = null,
                    ["error"] = new JsonObject
                    {
                        ["code"] = -32603,
                        ["message"] = ex.Message
                    }
                }, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Process a single JSON-RPC request line (also used by tests).</summary>
    public async Task<JsonObject?> HandleLineAsync(string line, CancellationToken ct = default)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(line);
        }
        catch (JsonException ex)
        {
            var err = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = null,
                ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "Parse error: " + ex.Message }
            };
            await WriteAsync(err, ct).ConfigureAwait(false);
            return err;
        }

        if (root is not JsonObject req)
            return null;

        var method = req["method"]?.GetValue<string>();
        var idNode = req["id"];
        // Notifications have no response
        var isNotification = idNode is null && method != null && method.StartsWith("notifications/", StringComparison.Ordinal);

        if (string.IsNullOrEmpty(method))
        {
            var err = Error(idNode, -32600, "Invalid Request: missing method");
            await WriteAsync(err, ct).ConfigureAwait(false);
            return err;
        }

        if (isNotification || method == "notifications/initialized")
            return null;

        JsonObject response;
        try
        {
            response = method switch
            {
                "initialize" => Ok(idNode, InitializeResult()),
                "ping" => Ok(idNode, new JsonObject()),
                "tools/list" => Ok(idNode, ToolsListResult()),
                "tools/call" => Ok(idNode, await ToolsCallResult(req["params"] as JsonObject, ct).ConfigureAwait(false)),
                "resources/list" => Ok(idNode, ResourcesListResult()),
                "resources/read" => Ok(idNode, ResourcesReadResult(req["params"] as JsonObject)),
                "prompts/list" => Ok(idNode, PromptsListResult()),
                "prompts/get" => Ok(idNode, PromptsGetResult(req["params"] as JsonObject)),
                _ => Error(idNode, -32601, $"Method not found: {method}")
            };
        }
        catch (Exception ex)
        {
            response = Error(idNode, -32603, ex.Message);
        }

        await WriteAsync(response, ct).ConfigureAwait(false);
        return response;
    }

    private JsonObject InitializeResult() => new()
    {
        ["protocolVersion"] = "2024-11-05",
        ["capabilities"] = new JsonObject
        {
            ["tools"] = new JsonObject { ["listChanged"] = true },
            ["resources"] = new JsonObject { ["listChanged"] = false },
            ["prompts"] = new JsonObject { ["listChanged"] = false }
        },
        ["serverInfo"] = new JsonObject
        {
            ["name"] = "unity-comdr",
            ["version"] = "0.2.0"
        },
        ["instructions"] =
            "Unity-Comdr: local-first Unity Editor MCP (no Python/Node/cloud). " +
            "Default ≤15 core tools for console/scripts/scene/GO/assets. " +
            "Use skill_manage action=list|load to unlock playmode, packages, menu, profiling, screenshots, batch, testing. " +
            "Use resources/list (unity://hierarchy, unity://console, …) and prompts/list for guided workflows. " +
            "Escape hatches (reflect_call/execute_code) off until escape_hatches_set enabled=true."
    };

    private JsonObject ToolsListResult()
    {
        var tools = new JsonArray();
        foreach (var t in _runtime.Registry.GetActiveTools())
        {
            tools.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = t.InputSchema.DeepClone()
            });
        }
        return new JsonObject { ["tools"] = tools };
    }

    private async Task<JsonObject> ToolsCallResult(JsonObject? paramsObj, CancellationToken ct)
    {
        if (paramsObj == null)
            throw new ArgumentException("tools/call requires params");
        var name = paramsObj["name"]?.GetValue<string>()
            ?? throw new ArgumentException("tools/call requires name");
        JsonObject? args = null;
        if (paramsObj["arguments"] is JsonObject ao)
            args = ao;
        else if (paramsObj["arguments"] is JsonValue jv && jv.GetValueKind() == JsonValueKind.String)
        {
            var parsed = JsonNode.Parse(jv.GetValue<string>());
            args = parsed as JsonObject;
        }

        ToolResult result;
        try
        {
            result = await _runtime.Registry.CallAsync(name, args, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = ToolResult.Error(ex.Message);
        }

        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = result.Content
            }
        };

        return new JsonObject
        {
            ["content"] = content,
            ["isError"] = result.IsError
        };
    }

    private JsonObject ResourcesListResult()
    {
        var resources = new JsonArray();
        foreach (var r in _runtime.Resources.List())
        {
            resources.Add(new JsonObject
            {
                ["uri"] = r.Uri,
                ["name"] = r.Name,
                ["description"] = r.Description,
                ["mimeType"] = "application/json"
            });
        }
        return new JsonObject { ["resources"] = resources };
    }

    private JsonObject ResourcesReadResult(JsonObject? paramsObj)
    {
        var uri = paramsObj?["uri"]?.GetValue<string>()
            ?? throw new ArgumentException("resources/read requires uri");
        var text = _runtime.Resources.Read(uri);
        return new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = "application/json",
                    ["text"] = text
                }
            }
        };
    }

    private JsonObject PromptsListResult()
    {
        var prompts = new JsonArray();
        foreach (var p in _runtime.Prompts.List())
        {
            prompts.Add(new JsonObject
            {
                ["name"] = p.Name,
                ["description"] = p.Description,
                ["title"] = p.Title
            });
        }
        return new JsonObject { ["prompts"] = prompts };
    }

    private JsonObject PromptsGetResult(JsonObject? paramsObj)
    {
        var name = paramsObj?["name"]?.GetValue<string>()
            ?? throw new ArgumentException("prompts/get requires name");
        var text = _runtime.Prompts.Get(name);
        return new JsonObject
        {
            ["description"] = _runtime.Prompts.List().FirstOrDefault(p => p.Name == name)?.Description ?? name,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = text
                    }
                }
            }
        };
    }

    private static JsonObject Ok(JsonNode? id, JsonObject result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        }
    };

    private async Task WriteAsync(JsonObject message, CancellationToken ct)
    {
        var json = message.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        await _output.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
        await _output.FlushAsync(ct).ConfigureAwait(false);
    }
}
