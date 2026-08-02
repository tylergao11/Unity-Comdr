using System.Text.Json.Nodes;
using UnityComdr.Editor;
using UnityComdr.Models;
using UnityComdr.Util;

namespace UnityComdr.Tools;

/// <summary>
/// Registers the token-frugal core tool set (≤ <see cref="ToolBudget.MaxDefaultCoreTools"/>).
/// </summary>
public static class CoreTools
{
    public static void RegisterAll(ToolRegistry registry, IEditorHost editor)
    {
        registry.RegisterCore(Make(
            "console_read",
            "Read Unity Console logs with optional type filter and pagination (token-frugal).",
            JsonSchemaHelper.Object(
                ("type", JsonSchemaHelper.String("Log, Warning, Error, or omit for all", new[] { "Log", "Warning", "Error" }), false),
                ("offset", JsonSchemaHelper.Integer("Start index"), false),
                ("pageSize", JsonSchemaHelper.Integer("Max entries (default 20)"), false),
                ("contains", JsonSchemaHelper.String("Substring filter on message"), false)
            ),
            async (args, _) =>
            {
                var logs = editor.GetConsoleLogs().AsEnumerable();
                var typeFilter = GetString(args, "type");
                if (!string.IsNullOrEmpty(typeFilter) && Enum.TryParse<LogType>(typeFilter, true, out var lt))
                    logs = logs.Where(l => l.Type == lt);
                var contains = GetString(args, "contains");
                if (!string.IsNullOrEmpty(contains))
                    logs = logs.Where(l => l.Message.Contains(contains, StringComparison.OrdinalIgnoreCase));
                var list = logs.ToList();
                var page = CompactResults.Paginate(list, GetInt(args, "offset"), GetInt(args, "pageSize"), l => new
                {
                    type = l.Type.ToString(),
                    message = l.Message,
                    file = l.File,
                    line = l.Line
                });
                return await Task.FromResult(ToolResult.OkJson(page));
            }));

        registry.RegisterCore(Make(
            "console_clear",
            "Clear the Unity Console log buffer.",
            JsonSchemaHelper.Object(),
            async (_, _) =>
            {
                editor.ClearConsole();
                return await Task.FromResult(ToolResult.Ok("Console cleared."));
            }));

        registry.RegisterCore(Make(
            "script_read",
            "Read a C# script under Assets/.",
            JsonSchemaHelper.Object(
                ("path", JsonSchemaHelper.String("Script path e.g. Assets/Scripts/Player.cs"), true)
            ),
            async (args, _) =>
            {
                var path = RequireString(args, "path");
                var content = editor.ReadScript(path);
                if (content == null)
                    return ToolResult.Error($"Script not found: {path}");
                return await Task.FromResult(ToolResult.Ok(CompactResults.Truncate(content), new { path, length = content.Length }));
            }));

        registry.RegisterCore(Make(
            "script_write",
            "Create or overwrite a C# script under Assets/. Triggers compile awareness.",
            JsonSchemaHelper.Object(
                ("path", JsonSchemaHelper.String("Target path"), true),
                ("content", JsonSchemaHelper.String("Full C# source"), true)
            ),
            async (args, _) =>
            {
                var path = RequireString(args, "path");
                var content = RequireString(args, "content");
                editor.WriteScript(path, content);
                return await Task.FromResult(ToolResult.OkJson(new { ok = true, path = NormalizeScriptPath(path) }));
            }));

        registry.RegisterCore(Make(
            "script_delete",
            "Delete a C# script asset.",
            JsonSchemaHelper.Object(
                ("path", JsonSchemaHelper.String("Script path"), true)
            ),
            async (args, _) =>
            {
                var path = RequireString(args, "path");
                var ok = editor.DeleteScript(path);
                return await Task.FromResult(ok
                    ? ToolResult.Ok($"Deleted {path}")
                    : ToolResult.Error($"Script not found: {path}"));
            }));

        registry.RegisterCore(Make(
            "script_list",
            "List script paths under Assets/ (paginated).",
            JsonSchemaHelper.Object(
                ("underPath", JsonSchemaHelper.String("Optional folder prefix"), false),
                ("offset", JsonSchemaHelper.Integer(), false),
                ("pageSize", JsonSchemaHelper.Integer(), false)
            ),
            async (args, _) =>
            {
                var list = editor.ListScripts(GetString(args, "underPath"));
                return await Task.FromResult(ToolResult.OkJson(
                    CompactResults.Paginate(list, GetInt(args, "offset"), GetInt(args, "pageSize"))));
            }));

        registry.RegisterCore(Make(
            "editor_state",
            "Get compile / play mode / active scene state.",
            JsonSchemaHelper.Object(),
            async (_, _) =>
            {
                var s = editor.GetState();
                return await Task.FromResult(ToolResult.OkJson(new
                {
                    isCompiling = s.IsCompiling,
                    isPlaying = s.IsPlaying,
                    isPaused = s.IsPaused,
                    activeScenePath = s.ActiveScenePath
                }));
            }));

        registry.RegisterCore(Make(
            "editor_compile",
            "Request script recompile and return updated editor state.",
            JsonSchemaHelper.Object(),
            async (_, _) =>
            {
                editor.RequestScriptCompile();
                var s = editor.GetState();
                return await Task.FromResult(ToolResult.OkJson(new
                {
                    ok = true,
                    isCompiling = s.IsCompiling,
                    message = "Compile requested."
                }));
            }));

        registry.RegisterCore(Make(
            "scene_manage",
            "Scene ops aligned with IvanMurzak/CoderGamester: get|list|list_opened|create|open|save|unload|set_active.",
            JsonSchemaHelper.Object(
                ("action", JsonSchemaHelper.String(null, new[]
                {
                    "get", "list", "list_opened", "create", "open", "save", "unload", "set_active"
                }), true),
                ("path", JsonSchemaHelper.String("Scene path"), false),
                ("name", JsonSchemaHelper.String("Scene name for create"), false),
                ("additive", JsonSchemaHelper.Boolean("Open additive (multi-scene)"), false)
            ),
            async (args, _) =>
            {
                var action = RequireString(args, "action").ToLowerInvariant();
                switch (action)
                {
                    case "get":
                    {
                        var sc = editor.GetActiveScene();
                        return ToolResult.OkJson(new { sc.Path, sc.Name, sc.Dirty, sc.IsLoaded, rootCount = sc.RootObjectIds.Count });
                    }
                    case "list":
                        return ToolResult.OkJson(editor.ListScenes().Select(s => new { s.Path, s.Name, s.Dirty, s.IsLoaded }));
                    case "list_opened":
                        return ToolResult.OkJson(editor.ListOpenedScenes().Select(s => new { s.Path, s.Name, s.Dirty, s.IsLoaded }));
                    case "create":
                    {
                        var path = RequireString(args, "path");
                        var sc = editor.CreateScene(path, GetString(args, "name"));
                        return ToolResult.OkJson(new { created = true, sc.Path, sc.Name });
                    }
                    case "open":
                    {
                        var path = RequireString(args, "path");
                        var additive = GetBool(args, "additive") ?? false;
                        var sc = editor.OpenScene(path, additive);
                        return ToolResult.OkJson(new { opened = true, additive, sc.Path, sc.Name });
                    }
                    case "save":
                        editor.SaveScene(GetString(args, "path"));
                        return ToolResult.OkJson(new { saved = true, path = editor.GetActiveScene().Path });
                    case "unload":
                    {
                        var path = RequireString(args, "path");
                        return editor.UnloadScene(path)
                            ? ToolResult.OkJson(new { unloaded = path })
                            : ToolResult.Error($"Cannot unload scene: {path}");
                    }
                    case "set_active":
                    {
                        var path = RequireString(args, "path");
                        return editor.SetActiveScene(path)
                            ? ToolResult.OkJson(new { active = path })
                            : ToolResult.Error($"Cannot set active scene: {path}");
                    }
                    default:
                        return ToolResult.Error($"Unknown action: {action}");
                }
            }));

        registry.RegisterCore(Make(
            "hierarchy_get",
            "Compact hierarchy summary of the active scene (paginated depth, truncated by default).",
            JsonSchemaHelper.Object(
                ("maxDepth", JsonSchemaHelper.Integer("Default 3"), false),
                ("maxNodes", JsonSchemaHelper.Integer("Default 40"), false)
            ),
            async (args, _) =>
            {
                var scene = editor.GetActiveScene();
                var all = editor.GetAllGameObjects();
                var maxDepth = GetInt(args, "maxDepth") ?? 3;
                var maxNodes = GetInt(args, "maxNodes") ?? 40;
                return await Task.FromResult(ToolResult.OkJson(
                    CompactResults.HierarchySummary(all, scene.RootObjectIds, maxDepth, maxNodes)));
            }));

        registry.RegisterCore(Make(
            "gameobject_manage",
            "GameObject ops (IvanMurzak/Coplay parity): create|get|find|delete|duplicate|rename|set_active|set_parent|set_transform|set_tag|set_layer.",
            JsonSchemaHelper.Object(
                ("action", JsonSchemaHelper.String(null, new[]
                {
                    "create", "get", "find", "delete", "duplicate", "rename",
                    "set_active", "set_parent", "set_transform", "set_tag", "set_layer"
                }), true),
                ("target", JsonSchemaHelper.String("Id or hierarchy path"), false),
                ("name", JsonSchemaHelper.String("Name for create/rename/find"), false),
                ("parent", JsonSchemaHelper.String("Parent id/path"), false),
                ("primitive", JsonSchemaHelper.String("Cube|Sphere|Capsule|Plane|Quad|Cylinder"), false),
                ("tag", JsonSchemaHelper.String("Unity tag"), false),
                ("layer", JsonSchemaHelper.Integer("Unity layer index"), false),
                ("componentType", JsonSchemaHelper.String("Filter find by component type"), false),
                ("active", JsonSchemaHelper.Boolean(), false),
                ("position", JsonSchemaHelper.ObjectOpen("x,y,z — partial axes allowed"), false),
                ("rotation", JsonSchemaHelper.ObjectOpen("x,y,z euler — partial axes allowed"), false),
                ("scale", JsonSchemaHelper.ObjectOpen("x,y,z — partial axes allowed"), false)
            ),
            async (args, _) =>
            {
                var action = RequireString(args, "action").ToLowerInvariant();
                switch (action)
                {
                    case "create":
                    {
                        var name = RequireString(args, "name");
                        var go = editor.CreateGameObject(name, GetString(args, "parent"), GetString(args, "primitive"));
                        var tag = GetString(args, "tag");
                        if (!string.IsNullOrEmpty(tag)) editor.SetTag(go.Id, tag!);
                        var layer = GetInt(args, "layer");
                        if (layer.HasValue) editor.SetLayer(go.Id, layer.Value);
                        go = editor.FindGameObject(go.Id)!;
                        return ToolResult.OkJson(SummarizeGo(go));
                    }
                    case "get":
                    {
                        var target = RequireString(args, "target");
                        var go = editor.FindGameObject(target);
                        return go == null
                            ? ToolResult.Error($"Not found: {target}")
                            : ToolResult.OkJson(DetailGo(go));
                    }
                    case "find":
                    {
                        var list = editor.FindGameObjects(GetString(args, "name"), GetString(args, "tag"), GetString(args, "componentType"));
                        return ToolResult.OkJson(CompactResults.Paginate(list, 0, 50, SummarizeGo));
                    }
                    case "delete":
                    {
                        var target = RequireString(args, "target");
                        return editor.DeleteGameObject(target)
                            ? ToolResult.Ok($"Deleted {target}")
                            : ToolResult.Error($"Not found: {target}");
                    }
                    case "duplicate":
                    {
                        var target = RequireString(args, "target");
                        var go = editor.DuplicateGameObject(target, GetString(args, "name"));
                        return go == null
                            ? ToolResult.Error($"Not found: {target}")
                            : ToolResult.OkJson(SummarizeGo(go));
                    }
                    case "rename":
                    {
                        var target = RequireString(args, "target");
                        var name = RequireString(args, "name");
                        return editor.RenameGameObject(target, name)
                            ? ToolResult.Ok($"Renamed to {name}")
                            : ToolResult.Error($"Not found: {target}");
                    }
                    case "set_active":
                    {
                        var target = RequireString(args, "target");
                        var active = GetBool(args, "active") ?? true;
                        return editor.SetActive(target, active)
                            ? ToolResult.Ok($"Active={active} for {target}")
                            : ToolResult.Error($"Not found: {target}");
                    }
                    case "set_parent":
                    {
                        var target = RequireString(args, "target");
                        var parent = GetString(args, "parent");
                        return editor.SetParent(target, parent)
                            ? ToolResult.Ok($"Parent set for {target}")
                            : ToolResult.Error($"Failed to set parent for {target}");
                    }
                    case "set_transform":
                    {
                        var target = RequireString(args, "target");
                        var existing = editor.FindGameObject(target);
                        if (existing == null)
                            return ToolResult.Error($"Not found: {target}");
                        var pos = MergeVec(existing.Transform.Position, ReadPartialVec(args, "position"));
                        var rot = MergeVec(existing.Transform.RotationEuler, ReadPartialVec(args, "rotation"));
                        var scl = MergeVec(existing.Transform.Scale, ReadPartialVec(args, "scale"));
                        var ok = editor.SetTransform(target, pos, rot, scl);
                        return ok ? ToolResult.Ok($"Transform updated for {target}") : ToolResult.Error($"Not found: {target}");
                    }
                    case "set_tag":
                    {
                        var target = RequireString(args, "target");
                        var tag = RequireString(args, "tag");
                        return editor.SetTag(target, tag)
                            ? ToolResult.Ok($"Tag={tag} for {target}")
                            : ToolResult.Error($"Not found: {target}");
                    }
                    case "set_layer":
                    {
                        var target = RequireString(args, "target");
                        var layer = GetInt(args, "layer") ?? 0;
                        return editor.SetLayer(target, layer)
                            ? ToolResult.Ok($"Layer={layer} for {target}")
                            : ToolResult.Error($"Not found: {target}");
                    }
                    default:
                        return ToolResult.Error($"Unknown action: {action}");
                }
            }));

        registry.RegisterCore(Make(
            "component_manage",
            "Component ops: add|get|modify|remove|list_types.",
            JsonSchemaHelper.Object(
                ("action", JsonSchemaHelper.String(null, new[] { "add", "get", "modify", "remove", "list_types" }), true),
                ("target", JsonSchemaHelper.String("GameObject id or path"), false),
                ("type", JsonSchemaHelper.String("Component type name e.g. Rigidbody"), false),
                ("filter", JsonSchemaHelper.String("Filter for list_types"), false),
                ("properties", JsonSchemaHelper.ObjectOpen("Key/value property bag"), false)
            ),
            async (args, _) =>
            {
                var action = RequireString(args, "action").ToLowerInvariant();
                if (action == "list_types")
                {
                    var types = editor.ListComponentTypes(GetString(args, "filter"));
                    return ToolResult.OkJson(new { types });
                }
                var target = RequireString(args, "target");
                var type = RequireString(args, "type");
                var props = ReadProps(args, "properties");
                switch (action)
                {
                    case "add":
                        return editor.AddComponent(target, type, props)
                            ? ToolResult.OkJson(new { ok = true, target, type })
                            : ToolResult.Error($"Failed to add {type} on {target}");
                    case "get":
                    {
                        var c = editor.GetComponent(target, type);
                        return c == null
                            ? ToolResult.Error($"Component {type} not found on {target}")
                            : ToolResult.OkJson(new { c.TypeName, c.Properties });
                    }
                    case "modify":
                        if (props == null || props.Count == 0)
                            return ToolResult.Error("properties required for modify");
                        return editor.ModifyComponent(target, type, props)
                            ? ToolResult.Ok($"Modified {type} on {target}")
                            : ToolResult.Error($"Failed to modify {type} on {target}");
                    case "remove":
                        return editor.RemoveComponent(target, type)
                            ? ToolResult.Ok($"Removed {type} from {target}")
                            : ToolResult.Error($"Component {type} not found on {target}");
                    default:
                        return ToolResult.Error($"Unknown action: {action}");
                }
            }));

        registry.RegisterCore(Make(
            "assets_manage",
            "Asset ops (IvanMurzak parity): find|material_create|material_assign|prefab_create|prefab_instantiate|create_folder|delete|copy|move|refresh|list_shaders.",
            JsonSchemaHelper.Object(
                ("action", JsonSchemaHelper.String(null, new[]
                {
                    "find", "material_create", "material_assign", "prefab_create", "prefab_instantiate",
                    "create_folder", "delete", "copy", "move", "refresh", "list_shaders"
                }), true),
                ("filter", JsonSchemaHelper.String("Glob filter e.g. Assets/**/*.cs"), false),
                ("kind", JsonSchemaHelper.String("Script|Material|Prefab|Folder"), false),
                ("path", JsonSchemaHelper.String("Asset path"), false),
                ("fromPath", JsonSchemaHelper.String("Source path for copy/move"), false),
                ("toPath", JsonSchemaHelper.String("Dest path for copy/move"), false),
                ("color", JsonSchemaHelper.String("Material color hex"), false),
                ("shader", JsonSchemaHelper.String("Shader name"), false),
                ("target", JsonSchemaHelper.String("GameObject for assign / prefab source"), false),
                ("parent", JsonSchemaHelper.String("Parent for instantiate"), false),
                ("offset", JsonSchemaHelper.Integer(), false),
                ("pageSize", JsonSchemaHelper.Integer(), false)
            ),
            async (args, _) =>
            {
                var action = RequireString(args, "action").ToLowerInvariant();
                switch (action)
                {
                    case "find":
                    {
                        var assets = editor.FindAssets(GetString(args, "filter"), GetString(args, "kind"));
                        return ToolResult.OkJson(CompactResults.Paginate(
                            assets,
                            GetInt(args, "offset"),
                            GetInt(args, "pageSize"),
                            a => new { a.Path, a.Kind, a.MaterialColor }));
                    }
                    case "material_create":
                    {
                        var path = RequireString(args, "path");
                        var mat = editor.CreateMaterial(path, GetString(args, "color"), GetString(args, "shader"));
                        return ToolResult.OkJson(mat);
                    }
                    case "material_assign":
                    {
                        var target = RequireString(args, "target");
                        var path = RequireString(args, "path");
                        return editor.AssignMaterial(target, path)
                            ? ToolResult.Ok($"Assigned {path} to {target}")
                            : ToolResult.Error("Assign failed (missing GO or material)");
                    }
                    case "prefab_create":
                    {
                        var path = RequireString(args, "path");
                        var target = RequireString(args, "target");
                        var prefab = editor.CreatePrefab(path, target);
                        return ToolResult.OkJson(prefab);
                    }
                    case "prefab_instantiate":
                    {
                        var path = RequireString(args, "path");
                        var go = editor.InstantiatePrefab(path, GetString(args, "parent"));
                        return go == null
                            ? ToolResult.Error($"Prefab not found: {path}")
                            : ToolResult.OkJson(SummarizeGo(go));
                    }
                    case "create_folder":
                    {
                        var path = RequireString(args, "path");
                        editor.CreateFolder(path);
                        return ToolResult.OkJson(new { created = path });
                    }
                    case "delete":
                    {
                        var path = RequireString(args, "path");
                        return editor.DeleteAsset(path)
                            ? ToolResult.Ok($"Deleted {path}")
                            : ToolResult.Error($"Asset not found: {path}");
                    }
                    case "copy":
                    {
                        var from = RequireString(args, "fromPath");
                        var to = RequireString(args, "toPath");
                        return editor.CopyAsset(from, to)
                            ? ToolResult.OkJson(new { copied = true, from, to })
                            : ToolResult.Error($"Copy failed: {from} -> {to}");
                    }
                    case "move":
                    {
                        var from = RequireString(args, "fromPath");
                        var to = RequireString(args, "toPath");
                        return editor.MoveAsset(from, to)
                            ? ToolResult.OkJson(new { moved = true, from, to })
                            : ToolResult.Error($"Move failed: {from} -> {to}");
                    }
                    case "refresh":
                        editor.RefreshAssets();
                        return ToolResult.Ok("AssetDatabase refreshed.");
                    case "list_shaders":
                        return ToolResult.OkJson(new { shaders = editor.ListShaders() });
                    default:
                        return ToolResult.Error($"Unknown action: {action}");
                }
            }));

        // Skill control stays in core so agents can expand capability without bloating the default schema.
        registry.RegisterCore(Make(
            "skill_manage",
            "List domain skills or load/unload one by id. Skills are not loaded by default (token-frugal).",
            JsonSchemaHelper.Object(
                ("action", JsonSchemaHelper.String(null, new[] { "list", "load", "unload" }), true),
                ("id", JsonSchemaHelper.String("Skill id for load/unload"), false)
            ),
            async (args, _) =>
            {
                var action = RequireString(args, "action").ToLowerInvariant();
                switch (action)
                {
                    case "list":
                    {
                        var skills = registry.ListSkills().Select(s => new
                        {
                            s.Id,
                            s.Name,
                            s.Description,
                            toolCount = s.Tools.Count,
                            loaded = registry.LoadedSkillIds.Contains(s.Id)
                        });
                        return await Task.FromResult(ToolResult.OkJson(new { skills, loaded = registry.LoadedSkillIds }));
                    }
                    case "load":
                    {
                        var id = RequireString(args, "id");
                        if (!registry.LoadSkill(id))
                            return ToolResult.Error($"Unknown skill: {id}");
                        var skill = registry.GetSkill(id)!;
                        return await Task.FromResult(ToolResult.OkJson(new
                        {
                            loaded = id,
                            tools = skill.Tools.Select(t => t.Name).ToList(),
                            activeTools = registry.ActiveToolCount
                        }));
                    }
                    case "unload":
                    {
                        var id = RequireString(args, "id");
                        var removed = registry.UnloadSkill(id);
                        return await Task.FromResult(removed
                            ? ToolResult.OkJson(new { unloaded = id, activeTools = registry.ActiveToolCount })
                            : ToolResult.Error($"Skill not loaded: {id}"));
                    }
                    default:
                        return ToolResult.Error($"Unknown action: {action}");
                }
            }));
    }

    public static void RegisterEscapeHatches(ToolRegistry registry, IEditorHost editor)
    {
        registry.RegisterEscapeHatch(Make(
            "reflect_call",
            "ESCAPE HATCH (off by default): describe a reflective call. Headless returns a dry-run plan.",
            JsonSchemaHelper.Object(
                ("typeName", JsonSchemaHelper.String("CLR type name"), true),
                ("methodName", JsonSchemaHelper.String("Method name"), true),
                ("argsJson", JsonSchemaHelper.String("JSON array of arguments"), false)
            ),
            async (args, _) =>
            {
                var typeName = RequireString(args, "typeName");
                var method = RequireString(args, "methodName");
                return await Task.FromResult(ToolResult.OkJson(new
                {
                    dryRun = true,
                    typeName,
                    method,
                    note = "Live reflection executes only under UnityEditorHost with escape hatches enabled."
                }));
            }));

        registry.RegisterEscapeHatch(Make(
            "execute_code",
            "ESCAPE HATCH (off by default): execute a short C# snippet (sandboxed/denied in headless).",
            JsonSchemaHelper.Object(
                ("code", JsonSchemaHelper.String("C# snippet"), true)
            ),
            async (args, _) =>
            {
                var code = RequireString(args, "code");
                return await Task.FromResult(ToolResult.OkJson(new
                {
                    accepted = false,
                    dryRun = true,
                    codeLength = code.Length,
                    note = "Dynamic execute is gated; enable escape hatches and use Unity Editor host for real runs."
                }));
            }));

        registry.RegisterCore(Make(
            "escape_hatches_set",
            "Enable or disable restricted escape hatch tools (reflect_call, execute_code). Default: disabled.",
            JsonSchemaHelper.Object(
                ("enabled", JsonSchemaHelper.Boolean("true to enable"), true)
            ),
            async (args, _) =>
            {
                var enabled = GetBool(args, "enabled") ?? false;
                registry.EscapeHatchesEnabled = enabled;
                return await Task.FromResult(ToolResult.OkJson(new
                {
                    escapeHatchesEnabled = enabled,
                    activeTools = registry.ActiveToolCount
                }));
            }));
    }

    // --- helpers ---

    private static ToolDefinition Make(
        string name,
        string description,
        JsonObject schema,
        Func<JsonObject?, CancellationToken, Task<ToolResult>> handler) =>
        new()
        {
            Name = name,
            Description = description,
            InputSchema = schema,
            Handler = handler
        };

    private static object SummarizeGo(GameObjectData go) => new
    {
        go.Id,
        go.Name,
        go.ParentId,
        go.Active,
        go.Tag,
        go.Layer,
        position = go.Transform.Position,
        components = go.Components.Select(c => c.TypeName).ToList()
    };

    private static object DetailGo(GameObjectData go) => new
    {
        go.Id,
        go.Name,
        go.ParentId,
        go.Active,
        go.Tag,
        go.Layer,
        transform = go.Transform,
        components = go.Components.Select(c => new { c.TypeName, c.Properties }),
        go.ChildIds
    };

    private static string? GetString(JsonObject? args, string key)
    {
        if (args == null || !args.TryGetPropertyValue(key, out var n) || n is null) return null;
        return n.GetValue<string>();
    }

    private static string RequireString(JsonObject? args, string key) =>
        GetString(args, key) ?? throw new ArgumentException($"Missing required argument: {key}");

    private static int? GetInt(JsonObject? args, string key)
    {
        if (args == null || !args.TryGetPropertyValue(key, out var n) || n is null) return null;
        if (n is JsonValue v && v.TryGetValue<int>(out var i)) return i;
        if (n is JsonValue v2 && v2.TryGetValue<long>(out var l)) return (int)l;
        return int.TryParse(n.ToString(), out var p) ? p : null;
    }

    private static bool? GetBool(JsonObject? args, string key)
    {
        if (args == null || !args.TryGetPropertyValue(key, out var n) || n is null) return null;
        if (n is JsonValue v && v.TryGetValue<bool>(out var b)) return b;
        return bool.TryParse(n.ToString(), out var p) ? p : null;
    }

    /// <summary>Per-axis optional floats; missing keys stay null so callers can merge with current transform.</summary>
    private static (float? X, float? Y, float? Z)? ReadPartialVec(JsonObject? args, string key)
    {
        if (args == null || !args.TryGetPropertyValue(key, out var n) || n is not JsonObject o) return null;
        float? Axis(string k)
        {
            if (!o.TryGetPropertyValue(k, out var v) || v is null) return null;
            if (v is JsonValue jv)
            {
                if (jv.TryGetValue<float>(out var f)) return f;
                if (jv.TryGetValue<double>(out var d)) return (float)d;
                if (jv.TryGetValue<int>(out var i)) return i;
            }
            return float.TryParse(v.ToString(), out var p) ? p : null;
        }
        var x = Axis("x");
        var y = Axis("y");
        var z = Axis("z");
        if (x is null && y is null && z is null) return null;
        return (x, y, z);
    }

    private static Vector3? MergeVec(Vector3 current, (float? X, float? Y, float? Z)? partial)
    {
        if (partial is null) return null; // no update for this channel
        var p = partial.Value;
        return new Vector3(p.X ?? current.X, p.Y ?? current.Y, p.Z ?? current.Z);
    }

    private static Dictionary<string, object?>? ReadProps(JsonObject? args, string key)
    {
        if (args == null || !args.TryGetPropertyValue(key, out var n) || n is not JsonObject o) return null;
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in o)
            dict[kv.Key] = kv.Value is JsonValue jv ? jv.ToString() : kv.Value?.ToJsonString();
        return dict;
    }

    private static string NormalizeScriptPath(string path)
    {
        path = path.Replace('\\', '/');
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) path += ".cs";
        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) path = "Assets/" + path.TrimStart('/');
        return path;
    }
}
