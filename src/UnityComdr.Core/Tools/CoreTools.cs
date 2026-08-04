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
                    line = l.Line,
                    epoch = l.Epoch,
                    stale = l.Stale
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
                var normalized = NormalizeScriptPath(path);
                var written = editor.ReadScript(normalized) ?? content;
                return await Task.FromResult(ToolResult.OkJson(new { path = normalized, length = written.Length }));
            }));

        registry.RegisterCore(Make(
            "script_delete",
            "Delete a C# script asset. Pass dryRun=true to preview impact without deleting.",
            JsonSchemaHelper.Object(
                ("path", JsonSchemaHelper.String("Script path"), true),
                ("dryRun", JsonSchemaHelper.Boolean("Preview impact without mutating (default false)"), false)
            ),
            async (args, _) =>
            {
                var path = RequireString(args, "path");
                var dryRun = GetBool(args, "dryRun") ?? false;
                var existing = editor.ReadScript(path);
                if (existing == null)
                    return ToolResult.Error($"Script not found: {path}");
                var impact = new[] { new { path = NormalizeScriptPath(path), length = existing.Length } };
                if (dryRun)
                    return await Task.FromResult(ToolResult.OkJson(new { dryRun = true, wouldDelete = impact }));
                var ok = editor.DeleteScript(path);
                return await Task.FromResult(ok
                    ? ToolResult.OkJson(new { deleted = true, removed = impact })
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
            "Get Editor lifecycle + compile/play/scene state. hostMode is live|headless (never treat headless as live Unity control). phase is connected|editor_compiling|editor_reloading|play_transition|editor_gone. compileEpoch marks console log generations; sessionGeneration invalidates prior GameObject instance ids after domain reload. When busy, wait suggestedRetrySeconds and retry — never treat hang/timeout as success.",
            JsonSchemaHelper.Object(),
            async (_, _) =>
            {
                var s = editor.GetState();
                if (string.IsNullOrWhiteSpace(s.HostMode))
                    s.HostMode = editor.HostMode;
                return await Task.FromResult(ToolResult.OkJson(new
                {
                    hostMode = s.HostMode,
                    hostDetail = s.HostDetail,
                    phase = s.Phase,
                    suggestedRetrySeconds = s.SuggestedRetrySeconds,
                    isCompiling = s.IsCompiling,
                    isPlaying = s.IsPlaying,
                    isPaused = s.IsPaused,
                    activeScenePath = s.ActiveScenePath,
                    compileEpoch = s.CompileEpoch,
                    sessionGeneration = s.SessionGeneration
                }));
            }));

        registry.RegisterCore(Make(
            "editor_compile",
            "Request script recompile via Editor compilation pipeline when live (CompilationPipeline.RequestScriptCompilation); returns compileEpoch. Console entries from older epochs are marked stale:true on console_read.",
            JsonSchemaHelper.Object(),
            async (_, _) =>
            {
                editor.RequestScriptCompile();
                var s = editor.GetState();
                return await Task.FromResult(ToolResult.OkJson(new
                {
                    ok = true,
                    hostMode = editor.HostMode,
                    compileEpoch = s.CompileEpoch,
                    isCompiling = s.IsCompiling,
                    message = string.Equals(editor.HostMode, "live", StringComparison.OrdinalIgnoreCase)
                        ? "Compile requested (CompilationPipeline)."
                        : "Headless compile epoch bumped (synthetic)."
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
            "GameObject ops (IvanMurzak/Coplay parity): create|get|find|delete|duplicate|rename|set_active|set_parent|set_transform|set_tag|set_layer. Mutations return post-state summary. delete supports dryRun.",
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
                ("dryRun", JsonSchemaHelper.Boolean("For delete: preview impact without mutating"), false),
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
                        return ToolResult.OkJson(MutationEcho(go));
                    }
                    case "get":
                    {
                        var target = RequireString(args, "target");
                        var go = editor.FindGameObject(target);
                        return go == null
                            ? NotFoundGo(editor, target)
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
                        var dryRun = GetBool(args, "dryRun") ?? false;
                        var existing = editor.FindGameObject(target);
                        if (existing == null)
                            return NotFoundGo(editor, target);
                        var impact = CollectGoSubtree(editor, existing);
                        if (dryRun)
                            return ToolResult.OkJson(new { dryRun = true, wouldDelete = impact });
                        editor.DeleteGameObject(target);
                        return ToolResult.OkJson(new { deleted = true, removed = impact });
                    }
                    case "duplicate":
                    {
                        var target = RequireString(args, "target");
                        var go = editor.DuplicateGameObject(target, GetString(args, "name"));
                        return go == null
                            ? NotFoundGo(editor, target)
                            : ToolResult.OkJson(MutationEcho(go));
                    }
                    case "rename":
                    {
                        var target = RequireString(args, "target");
                        var name = RequireString(args, "name");
                        if (!editor.RenameGameObject(target, name))
                            return NotFoundGo(editor, target);
                        var go = editor.FindGameObject(target) ?? editor.FindGameObject(name);
                        return go == null
                            ? ToolResult.Error($"Renamed but could not re-read: {name}")
                            : ToolResult.OkJson(MutationEcho(go));
                    }
                    case "set_active":
                    {
                        var target = RequireString(args, "target");
                        var active = GetBool(args, "active") ?? true;
                        if (!editor.SetActive(target, active))
                            return NotFoundGo(editor, target);
                        return ToolResult.OkJson(MutationEcho(editor.FindGameObject(target)!));
                    }
                    case "set_parent":
                    {
                        var target = RequireString(args, "target");
                        var parent = GetString(args, "parent");
                        if (!editor.SetParent(target, parent))
                            return ToolResult.ErrorEnvelope(
                                "set_parent_failed",
                                $"Failed to set parent for {target}",
                                suggestion: "Ensure target and parent ids/paths exist and are not the same object.",
                                nextStep: "Call gameobject_manage action=get on target/parent, then retry.");
                        return ToolResult.OkJson(MutationEcho(editor.FindGameObject(target)!));
                    }
                    case "set_transform":
                    {
                        var target = RequireString(args, "target");
                        var existing = editor.FindGameObject(target);
                        if (existing == null)
                            return NotFoundGo(editor, target);
                        var pos = MergeVec(existing.Transform.Position, ReadPartialVec(args, "position"));
                        var rot = MergeVec(existing.Transform.RotationEuler, ReadPartialVec(args, "rotation"));
                        var scl = MergeVec(existing.Transform.Scale, ReadPartialVec(args, "scale"));
                        if (!editor.SetTransform(target, pos, rot, scl))
                            return NotFoundGo(editor, target);
                        return ToolResult.OkJson(MutationEcho(editor.FindGameObject(target)!));
                    }
                    case "set_tag":
                    {
                        var target = RequireString(args, "target");
                        var tag = RequireString(args, "tag");
                        if (!editor.SetTag(target, tag))
                            return NotFoundGo(editor, target);
                        return ToolResult.OkJson(MutationEcho(editor.FindGameObject(target)!));
                    }
                    case "set_layer":
                    {
                        var target = RequireString(args, "target");
                        var layer = GetInt(args, "layer") ?? 0;
                        if (!editor.SetLayer(target, layer))
                            return NotFoundGo(editor, target);
                        return ToolResult.OkJson(MutationEcho(editor.FindGameObject(target)!));
                    }
                    default:
                        return ToolResult.Error($"Unknown action: {action}");
                }
            }));

        registry.RegisterCore(Make(
            "component_manage",
            "Component ops: add|get|modify|remove|list_types. Live get exports SerializedObject properties (bounded); list_types scans loaded Component types. modify supports scalar + Vector2/3/Color/Enum. UI/RectTransform mutations return layout summary + vision nextStep for region crop re-check (AC-V10).",
            JsonSchemaHelper.Object(
                ("action", JsonSchemaHelper.String(null, new[] { "add", "get", "modify", "remove", "list_types" }), true),
                ("target", JsonSchemaHelper.String("GameObject id or path"), false),
                ("type", JsonSchemaHelper.String("Component type name e.g. Rigidbody or RectTransform"), false),
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
                    {
                        if (!editor.AddComponent(target, type, props))
                            return ToolResult.Error($"Failed to add {type} on {target}");
                        var added = editor.GetComponent(target, type);
                        object snapshot = added == null
                            ? new { typeName = type, properties = props ?? new Dictionary<string, object?>() }
                            : ComponentSnapshot(added);
                        return ToolResult.OkJson(MutationComponentResult(target, type, snapshot, added));
                    }
                    case "get":
                    {
                        var c = editor.GetComponent(target, type);
                        return c == null
                            ? ToolResult.Error($"Component {type} not found on {target}")
                            : ToolResult.OkJson(MutationComponentResult(target, type, ComponentSnapshot(c), c));
                    }
                    case "modify":
                    {
                        if (props == null || props.Count == 0)
                            return ToolResult.Error("properties required for modify");
                        if (!editor.ModifyComponent(target, type, props))
                            return ToolResult.Error($"Failed to modify {type} on {target}");
                        var modified = editor.GetComponent(target, type);
                        object snapshot = modified == null
                            ? new { typeName = type, properties = props }
                            : ComponentSnapshot(modified);
                        return ToolResult.OkJson(MutationComponentResult(target, type, snapshot, modified));
                    }
                    case "remove":
                    {
                        var before = editor.GetComponent(target, type);
                        if (before == null || !editor.RemoveComponent(target, type))
                            return ToolResult.Error($"Component {type} not found on {target}");
                        return ToolResult.OkJson(new { removed = true, target, component = ComponentSnapshot(before) });
                    }
                    default:
                        return ToolResult.Error($"Unknown action: {action}");
                }
            }));

        registry.RegisterCore(Make(
            "assets_manage",
            "Asset ops (IvanMurzak parity): find|material_create|material_assign|prefab_create|prefab_instantiate|create_folder|delete|copy|move|refresh|list_shaders. delete supports dryRun.",
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
                ("dryRun", JsonSchemaHelper.Boolean("For delete: preview impact without mutating"), false),
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
                        var dryRun = GetBool(args, "dryRun") ?? false;
                        var normalized = path.Replace('\\', '/');
                        var matches = editor.FindAssets()
                            .Where(a => a.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                                        a.Path.StartsWith(normalized.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                            .Select(a => (object)new { path = a.Path, kind = a.Kind })
                            .ToList();
                        if (matches.Count == 0)
                            return ToolResult.Error($"Asset not found: {path}");
                        if (dryRun)
                            return ToolResult.OkJson(new { dryRun = true, wouldDelete = matches });
                        return editor.DeleteAsset(path)
                            ? ToolResult.OkJson(new { deleted = true, removed = matches })
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
            "ESCAPE HATCH (off by default): PLAN-ONLY. Returns a dry-run plan; does NOT invoke methods. Not live reflection.",
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
                    planOnly = true,
                    executed = false,
                    dryRun = true,
                    typeName,
                    method,
                    hostMode = editor.HostMode,
                    note = "plan-only: no reflection is executed in any hostMode. Claim NO for live execute."
                }));
            }));

        registry.RegisterEscapeHatch(Make(
            "execute_code",
            "ESCAPE HATCH (off by default): PLAN-ONLY. Does NOT run C#; returns rejected plan. No Roslyn sandbox.",
            JsonSchemaHelper.Object(
                ("code", JsonSchemaHelper.String("C# snippet"), true)
            ),
            async (args, _) =>
            {
                var code = RequireString(args, "code");
                return await Task.FromResult(ToolResult.OkJson(new
                {
                    planOnly = true,
                    executed = false,
                    accepted = false,
                    dryRun = true,
                    codeLength = code.Length,
                    hostMode = editor.HostMode,
                    note = "plan-only: code is never executed. Claim NO for execute_code."
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

    /// <summary>O4 mutation echo: post-state summary agents can trust without a follow-up get.</summary>
    private static object MutationEcho(GameObjectData go) => new
    {
        id = go.Id,
        name = go.Name,
        transform = go.Transform,
        parent = go.ParentId,
        active = go.Active,
        tag = go.Tag,
        layer = go.Layer
    };

    private static object ComponentSnapshot(ComponentData c) => new
    {
        typeName = c.TypeName,
        properties = c.Properties
    };

    /// <summary>AC-V10: layout post-state + path to native region crop re-check after UI mutations.</summary>
    private static object MutationComponentResult(string target, string type, object snapshot, ComponentData? data)
    {
        var layout = TryLayoutSummary(type, data);
        if (layout == null)
            return new { target, component = snapshot };

        return new
        {
            target,
            component = snapshot,
            layout,
            vision = new
            {
                nextStep =
                    "Re-check pixels: skill_manage action=load id=screenshots; " +
                    "screenshot_capture source=game_view with regionX/regionY/regionWidth/regionHeight from layout " +
                    "(native resolution crop — maxResolution cost knob does NOT apply to regions).",
                regionNative = true
            }
        };
    }

    private static object? TryLayoutSummary(string type, ComponentData? data)
    {
        if (data?.Properties == null || data.Properties.Count == 0)
        {
            if (type.Contains("RectTransform", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("Layout", StringComparison.OrdinalIgnoreCase))
                return new { typeName = type, note = "layout type; properties empty — call get after live modify" };
            return null;
        }

        var layoutKeys = new[]
        {
            "anchorMin", "anchorMax", "anchoredPosition", "sizeDelta", "pivot",
            "offsetMin", "offsetMax", "anchorPos", "rect", "localPosition", "localScale"
        };
        var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in data.Properties)
        {
            if (layoutKeys.Any(k => kv.Key.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
                kv.Key.Contains("anchor", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Contains("offset", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Contains("size", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Contains("pivot", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Contains("rect", StringComparison.OrdinalIgnoreCase))
            {
                bag[kv.Key] = kv.Value;
            }
        }

        var isUiType = type.Contains("RectTransform", StringComparison.OrdinalIgnoreCase) ||
                       type.Contains("Layout", StringComparison.OrdinalIgnoreCase) ||
                       type.Contains("Canvas", StringComparison.OrdinalIgnoreCase);
        if (bag.Count == 0 && !isUiType)
            return null;

        return new
        {
            typeName = type,
            rect = bag.TryGetValue("rect", out var r) ? r : null,
            anchorMin = FindProp(bag, "anchorMin"),
            anchorMax = FindProp(bag, "anchorMax"),
            anchoredPosition = FindProp(bag, "anchoredPosition") ?? FindProp(bag, "anchorPos"),
            sizeDelta = FindProp(bag, "sizeDelta"),
            pivot = FindProp(bag, "pivot"),
            properties = bag.Count > 0 ? bag : data.Properties
        };
    }

    private static object? FindProp(Dictionary<string, object?> bag, string key) =>
        bag.TryGetValue(key, out var v) ? v : bag.FirstOrDefault(kv => kv.Key.Contains(key, StringComparison.OrdinalIgnoreCase)).Value;

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

    private static ToolResult NotFoundGo(IEditorHost editor, string target)
    {
        string? nearest = null;
        try
        {
            nearest = NameSuggest.Nearest(target, editor.GetAllGameObjects().Select(g => g.Name));
        }
        catch
        {
            // Live hosts may fail best-effort enumeration; still return actionable error.
        }

        var suggestion = nearest != null ? $"Did you mean '{nearest}'?" : null;
        var nextStep = nearest != null
            ? $"Retry gameobject_manage with target='{nearest}', or call hierarchy_get / action=find."
            : "Call hierarchy_get or gameobject_manage action=find to list known objects, then retry.";
        return ToolResult.ErrorEnvelope(
            "not_found",
            $"Not found: {target}",
            suggestion,
            nextStep);
    }

    private static List<object> CollectGoSubtree(IEditorHost editor, GameObjectData root)
    {
        var list = new List<object>();
        void Walk(GameObjectData g)
        {
            list.Add(new { id = g.Id, name = g.Name, parent = g.ParentId, active = g.Active });
            foreach (var childId in g.ChildIds)
            {
                var child = editor.FindGameObject(childId);
                if (child != null) Walk(child);
            }
        }
        Walk(root);
        return list;
    }

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
