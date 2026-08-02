using System.Text.Json.Nodes;

namespace UnityComdr.Tools;

public static class JsonSchemaHelper
{
    public static JsonObject Object(params (string name, JsonObject prop, bool required)[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, prop, req) in properties)
        {
            props[name] = prop;
            if (req) required.Add(name);
        }
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["additionalProperties"] = false
        };
        if (required.Count > 0)
            schema["required"] = required;
        return schema;
    }

    public static JsonObject String(string? description = null, string[]? enums = null)
    {
        var o = new JsonObject { ["type"] = "string" };
        if (description != null) o["description"] = description;
        if (enums != null)
        {
            var arr = new JsonArray();
            foreach (var e in enums) arr.Add(e);
            o["enum"] = arr;
        }
        return o;
    }

    public static JsonObject Integer(string? description = null)
    {
        var o = new JsonObject { ["type"] = "integer" };
        if (description != null) o["description"] = description;
        return o;
    }

    public static JsonObject Number(string? description = null)
    {
        var o = new JsonObject { ["type"] = "number" };
        if (description != null) o["description"] = description;
        return o;
    }

    public static JsonObject Boolean(string? description = null)
    {
        var o = new JsonObject { ["type"] = "boolean" };
        if (description != null) o["description"] = description;
        return o;
    }

    public static JsonObject ObjectOpen(string? description = null)
    {
        var o = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = true
        };
        if (description != null) o["description"] = description;
        return o;
    }
}
