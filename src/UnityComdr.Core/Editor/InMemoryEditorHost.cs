using System.Text.RegularExpressions;
using UnityComdr.Models;

namespace UnityComdr.Editor;

/// <summary>
/// Headless Editor stand-in used by tests and MCP host when Unity is not attached.
/// GameObjects are scoped per scene. Implements full IEditorHost surface for parity testing.
/// </summary>
public sealed class InMemoryEditorHost : IEditorHost
{
    private readonly List<ConsoleLogEntry> _logs = new();
    private readonly Dictionary<string, Dictionary<string, GameObjectData>> _objectsByScene =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScriptFile> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SceneData> _scenes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _openedScenes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MaterialData> _materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PrefabData> _prefabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameObjectData> _prefabTemplates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PackageInfo> _packages = new();
    private readonly List<MenuItemInfo> _menuItems = new();
    private readonly SelectionState _selection = new();
    private readonly EditorState _state = new();
    private readonly ProfilerSnapshot _profiler = new();
    private readonly Dictionary<string, ProfilerSnapshot> _profilerSaves = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UiControlInfo> _uiControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskLeaseManager _lease;
    private readonly List<string> _inputLog = new();
    private readonly HashSet<string> _staleObjectIds = new(StringComparer.OrdinalIgnoreCase);
    private SceneData _active;
    private int _frame;
    private string? _forcedPhase;
    private int _forcedRetrySeconds = 2;
    private int _compileEpoch;
    private int _sessionGeneration = 1;

    /// <summary>Optional clock injection for lease TTL tests.</summary>
    public InMemoryEditorHost(Func<DateTimeOffset>? clock = null)
        : this()
    {
        // Secondary ctor chains to parameterless which already built _lease with UtcNow.
        // Replace via reflection-free reassign: use field init pattern instead.
    }

    /// <summary>Headless host with injectable clock (lease TTL tests).</summary>
    public static InMemoryEditorHost CreateWithClock(Func<DateTimeOffset> clock) =>
        new InMemoryEditorHost(clock, seedUi: true);

    private Dictionary<string, GameObjectData> Objects => _objectsByScene[_active.Path];

    private static readonly string[] BuiltinShaders =
    {
        "Standard", "Unlit/Color", "Unlit/Texture", "Universal Render Pipeline/Lit",
        "Sprites/Default", "UI/Default", "Skybox/Procedural"
    };

    private static readonly string[] BuiltinComponentTypes =
    {
        "Transform", "Camera", "Light", "Rigidbody", "BoxCollider", "SphereCollider",
        "CapsuleCollider", "MeshRenderer", "MeshFilter", "AudioSource", "Animator",
        "CharacterController", "NavMeshAgent", "ParticleSystem", "Canvas", "Image", "Text"
    };

    public InMemoryEditorHost() : this(null, seedUi: true) { }

    private InMemoryEditorHost(Func<DateTimeOffset>? clock, bool seedUi)
    {
        _lease = new TaskLeaseManager(clock);
        _active = new SceneData
        {
            Path = "Assets/Scenes/SampleScene.unity",
            Name = "SampleScene",
            IsLoaded = true
        };
        _scenes[_active.Path] = _active;
        _objectsByScene[_active.Path] = new Dictionary<string, GameObjectData>(StringComparer.OrdinalIgnoreCase);
        _openedScenes.Add(_active.Path);
        _folders.Add("Assets");
        _folders.Add("Assets/Scenes");
        _folders.Add("Assets/Scripts");
        _folders.Add("Assets/Materials");
        _folders.Add("Assets/Prefabs");

        _packages.Add(new PackageInfo { Name = "com.unity.modules.ui", Version = "1.0.0", Source = "built-in", DisplayName = "UI" });
        _packages.Add(new PackageInfo { Name = "com.unity.textmeshpro", Version = "3.0.6", Source = "registry", DisplayName = "TextMeshPro" });
        _packages.Add(new PackageInfo { Name = "com.unity.ugui", Version = "1.0.0", Source = "registry", DisplayName = "Unity UI" });
        _packages.Add(new PackageInfo { Name = "com.unity.inputsystem", Version = "1.7.0", Source = "registry", DisplayName = "Input System" });
        _packages.Add(new PackageInfo { Name = "com.unitycomdr.mcp", Version = "0.2.0", Source = "local", DisplayName = "Unity-Comdr MCP" });

        _menuItems.AddRange(new[]
        {
            new MenuItemInfo { Path = "GameObject/Create Empty", Category = "GameObject" },
            new MenuItemInfo { Path = "GameObject/3D Object/Cube", Category = "GameObject" },
            new MenuItemInfo { Path = "GameObject/3D Object/Sphere", Category = "GameObject" },
            new MenuItemInfo { Path = "GameObject/3D Object/Plane", Category = "GameObject" },
            new MenuItemInfo { Path = "GameObject/Light/Directional Light", Category = "GameObject" },
            new MenuItemInfo { Path = "GameObject/Camera", Category = "GameObject" },
            new MenuItemInfo { Path = "Assets/Create/Folder", Category = "Assets" },
            new MenuItemInfo { Path = "Assets/Create/Material", Category = "Assets" },
            new MenuItemInfo { Path = "Assets/Create/C# Script", Category = "Assets" },
            new MenuItemInfo { Path = "Edit/Project Settings...", Category = "Edit" },
            new MenuItemInfo { Path = "Window/General/Console", Category = "Window" },
            new MenuItemInfo { Path = "Window/General/Test Runner", Category = "Window" },
            new MenuItemInfo { Path = "File/Save", Category = "File" },
            new MenuItemInfo { Path = "File/Save Project", Category = "File" }
        });

        var cam = CreateGameObjectInternal("Main Camera", null);
        cam.Tag = "MainCamera";
        AddComponent(cam.Id, "Camera");
        var light = CreateGameObjectInternal("Directional Light", null);
        AddComponent(light.Id, "Light");

        if (seedUi)
        {
            _uiControls["ui-score"] = new UiControlInfo
            {
                Id = "ui-score",
                Name = "ScoreLabel",
                Path = "Canvas/HUD/ScoreLabel",
                Kind = "Text",
                Interactable = false,
                Rect = new UiRect { X = 16, Y = 16, W = 120, H = 32 }
            };
            _uiControls["ui-play"] = new UiControlInfo
            {
                Id = "ui-play",
                Name = "PlayButton",
                Path = "Canvas/Menu/PlayButton",
                Kind = "Button",
                Interactable = true,
                Rect = new UiRect { X = 200, Y = 300, W = 160, H = 48 }
            };
        }
    }

    // --- Console ---

    public IReadOnlyList<ConsoleLogEntry> GetConsoleLogs() =>
        _logs.Select(l => l with { Stale = l.Epoch < _compileEpoch }).ToList();

    public void ClearConsole() => _logs.Clear();

    public void AddConsoleLog(ConsoleLogEntry entry)
    {
        var stamped = entry.Epoch == 0 ? entry with { Epoch = _compileEpoch } : entry;
        _logs.Add(stamped with { Stale = false });
    }

    // --- Editor state ---

    /// <summary>
    /// Test hook (FR-R1): force a busy lifecycle phase so tools return immediate busy errors.
    /// Pass null or <see cref="EditorLifecyclePhases.Connected"/> to clear.
    /// </summary>
    public void SimulateBusy(string? phase, int suggestedRetrySeconds = 2)
    {
        if (string.IsNullOrWhiteSpace(phase) ||
            string.Equals(phase, EditorLifecyclePhases.Connected, StringComparison.OrdinalIgnoreCase))
        {
            _forcedPhase = null;
            _state.IsCompiling = false;
            return;
        }

        _forcedPhase = phase;
        _forcedRetrySeconds = suggestedRetrySeconds > 0
            ? suggestedRetrySeconds
            : EditorLifecyclePhases.DefaultRetrySeconds(phase);
        _state.IsCompiling = string.Equals(phase, EditorLifecyclePhases.EditorCompiling, StringComparison.OrdinalIgnoreCase);
    }

    public void ClearBusy() => SimulateBusy(null);

    /// <summary>
    /// Test hook (O2 / FR-A5): simulate domain reload — bump sessionGeneration and retire prior GO ids.
    /// </summary>
    public void BumpSessionGeneration()
    {
        _sessionGeneration++;
        RemintAllObjectIds();
    }

    public EditorState GetState()
    {
        var phase = ResolvePhase();
        return new EditorState
        {
            IsCompiling = _state.IsCompiling ||
                          string.Equals(phase, EditorLifecyclePhases.EditorCompiling, StringComparison.OrdinalIgnoreCase),
            IsPlaying = _state.IsPlaying,
            IsPaused = _state.IsPaused,
            ActiveScenePath = _active.Path,
            Phase = phase,
            SuggestedRetrySeconds = EditorLifecyclePhases.IsBusy(phase) ? _forcedRetrySeconds : null,
            CompileEpoch = _compileEpoch,
            SessionGeneration = _sessionGeneration,
            HostMode = "headless",
            HostDetail = "InMemoryEditorHost — not connected to a live Unity Editor bridge."
        };
    }

    private string ResolvePhase()
    {
        if (!string.IsNullOrEmpty(_forcedPhase))
            return _forcedPhase!;
        if (_state.IsCompiling)
            return EditorLifecyclePhases.EditorCompiling;
        return EditorLifecyclePhases.Connected;
    }

    public void SetCompiling(bool compiling) => _state.IsCompiling = compiling;

    public void RequestScriptCompile()
    {
        _state.IsCompiling = true;
        // Recompile clears compile/file-scoped errors for scripts that exist after write (full code-fix loop).
        _logs.RemoveAll(IsClearedByRecompile);
        _compileEpoch++;
        _state.IsCompiling = false;
        AddConsoleLog(new ConsoleLogEntry(LogType.Log, "Scripts recompiled (headless).", Epoch: _compileEpoch));
    }

    private bool IsClearedByRecompile(ConsoleLogEntry log)
    {
        if (log.Type != LogType.Error) return false;
        if (log.Message.Contains("error CS", StringComparison.OrdinalIgnoreCase) ||
            log.Message.Contains(": error ", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(log.File))
        {
            var path = NormalizePath(log.File!);
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) path += ".cs";
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                path = "Assets/" + path.TrimStart('/');
            if (_scripts.ContainsKey(path)) return true;
        }
        return false;
    }

    public void SetPlayMode(bool playing, bool paused = false)
    {
        _state.IsPlaying = playing;
        _state.IsPaused = paused && playing;
        if (!playing) _state.IsPaused = false;
        AddConsoleLog(new ConsoleLogEntry(LogType.Log,
            playing ? (paused ? "Play mode paused." : "Entered play mode.") : "Exited play mode."));
    }

    public void StepPlayModeFrame()
    {
        if (!_state.IsPlaying) SetPlayMode(true, true);
        _frame++;
        _profiler.DeltaTimeMs = 16.67f;
        _profiler.Fps = 60f;
        AddConsoleLog(new ConsoleLogEntry(LogType.Log, $"Play mode stepped frame {_frame}."));
    }

    // --- Selection ---

    public SelectionState GetSelection() => new()
    {
        GameObjectIds = _selection.GameObjectIds.ToList(),
        AssetPaths = _selection.AssetPaths.ToList()
    };

    public void SetSelection(IReadOnlyList<string>? gameObjectIds = null, IReadOnlyList<string>? assetPaths = null)
    {
        _selection.GameObjectIds = gameObjectIds?.ToList() ?? new List<string>();
        _selection.AssetPaths = assetPaths?.ToList() ?? new List<string>();
    }

    // --- Scripts ---

    public IReadOnlyList<string> ListScripts(string? underPath = null)
    {
        var q = _scripts.Keys.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(underPath))
            q = q.Where(p => p.StartsWith(NormalizePath(underPath), StringComparison.OrdinalIgnoreCase));
        return q.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string? ReadScript(string path)
    {
        path = NormalizePath(path);
        return _scripts.TryGetValue(path, out var s) ? s.Content : null;
    }

    public void WriteScript(string path, string content)
    {
        path = NormalizePath(path);
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) path += ".cs";
        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) path = "Assets/" + path.TrimStart('/');
        EnsureParentFolder(path);
        _scripts[path] = new ScriptFile { Path = path, Content = content };
        _state.IsCompiling = false;
        // Writing a script invalidates prior errors that name this file (code-fix path).
        _logs.RemoveAll(l =>
            l.Type == LogType.Error &&
            ((!string.IsNullOrEmpty(l.File) && NormalizePath(l.File!).Equals(path, StringComparison.OrdinalIgnoreCase)) ||
             l.Message.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0));
        AddConsoleLog(new ConsoleLogEntry(LogType.Log, $"Script written: {path}"));
    }

    public bool DeleteScript(string path)
    {
        path = NormalizePath(path);
        return _scripts.Remove(path);
    }

    // --- Scenes ---

    public SceneData GetActiveScene() => CloneScene(_active);

    public IReadOnlyList<SceneData> ListScenes() =>
        _scenes.Values.Select(CloneScene).ToList();

    public IReadOnlyList<SceneData> ListOpenedScenes() =>
        _openedScenes.Where(p => _scenes.ContainsKey(p)).Select(p => CloneScene(_scenes[p])).ToList();

    public SceneData CreateScene(string path, string? name = null)
    {
        path = NormalizeAssetPath(path, ".unity");
        var scene = new SceneData
        {
            Path = path,
            Name = name ?? Path.GetFileNameWithoutExtension(path),
            IsLoaded = true
        };
        _scenes[path] = scene;
        _objectsByScene[path] = new Dictionary<string, GameObjectData>(StringComparer.OrdinalIgnoreCase);
        _openedScenes.Add(path);
        _active = scene;
        _state.ActiveScenePath = path;
        return CloneScene(scene);
    }

    public SceneData OpenScene(string path, bool additive = false)
    {
        path = NormalizePath(path);
        if (!_scenes.TryGetValue(path, out var scene))
            throw new InvalidOperationException($"Scene not found: {path}");
        if (!_objectsByScene.ContainsKey(path))
            _objectsByScene[path] = new Dictionary<string, GameObjectData>(StringComparer.OrdinalIgnoreCase);
        if (!additive)
        {
            // Single-mode open: only this scene remains "focused" as active; others stay loaded if were opened
            _openedScenes.Clear();
        }
        scene.IsLoaded = true;
        _openedScenes.Add(path);
        _active = scene;
        _state.ActiveScenePath = path;
        return CloneScene(scene);
    }

    public void SaveScene(string? path = null)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = NormalizePath(path!);
            if (!path.Equals(_active.Path, StringComparison.OrdinalIgnoreCase))
            {
                if (_objectsByScene.TryGetValue(_active.Path, out var bag))
                {
                    _objectsByScene.Remove(_active.Path);
                    _objectsByScene[path] = bag;
                }
                _openedScenes.Remove(_active.Path);
                _scenes.Remove(_active.Path);
                _active.Path = path;
                _scenes[path] = _active;
                _openedScenes.Add(path);
            }
            _state.ActiveScenePath = path;
        }
        _active.Dirty = false;
        AddConsoleLog(new ConsoleLogEntry(LogType.Log, $"Scene saved: {_active.Path}"));
    }

    public bool UnloadScene(string path)
    {
        path = NormalizePath(path);
        if (!_scenes.TryGetValue(path, out var scene)) return false;
        if (path.Equals(_active.Path, StringComparison.OrdinalIgnoreCase))
        {
            // Cannot unload active without switching — switch to another opened if any
            var other = _openedScenes.FirstOrDefault(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (other == null) return false;
            _active = _scenes[other];
            _state.ActiveScenePath = other;
        }
        scene.IsLoaded = false;
        _openedScenes.Remove(path);
        return true;
    }

    public bool SetActiveScene(string path)
    {
        path = NormalizePath(path);
        if (!_scenes.TryGetValue(path, out var scene) || !scene.IsLoaded) return false;
        _active = scene;
        _openedScenes.Add(path);
        _state.ActiveScenePath = path;
        return true;
    }

    // --- GameObjects ---

    public GameObjectData? FindGameObject(string idOrPath)
    {
        ThrowIfStaleObjectId(idOrPath);

        if (Objects.TryGetValue(idOrPath, out var byId))
            return CloneGo(byId);

        var parts = idOrPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        IEnumerable<GameObjectData> candidates = Objects.Values.Where(o => o.ParentId == null);
        GameObjectData? current = null;
        foreach (var part in parts)
        {
            current = candidates.FirstOrDefault(o => o.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (current == null) return null;
            candidates = current.ChildIds.Select(id => Objects[id]);
        }
        return current == null ? null : CloneGo(current);
    }

    public IReadOnlyList<GameObjectData> FindGameObjects(string? name = null, string? tag = null, string? componentType = null)
    {
        IEnumerable<GameObjectData> q = Objects.Values;
        if (!string.IsNullOrWhiteSpace(name))
            q = q.Where(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                             g.Name.Contains(name!, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(tag))
            q = q.Where(g => g.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(componentType))
            q = q.Where(g => g.Components.Any(c => c.TypeName.Equals(componentType, StringComparison.OrdinalIgnoreCase)));
        return q.Select(CloneGo).ToList();
    }

    public IReadOnlyList<GameObjectData> GetAllGameObjects() =>
        Objects.Values.Select(CloneGo).ToList();

    public GameObjectData CreateGameObject(string name, string? parentIdOrPath = null, string? primitiveType = null)
    {
        string? parentId = null;
        if (!string.IsNullOrWhiteSpace(parentIdOrPath))
        {
            var parent = FindGameObject(parentIdOrPath!) ?? throw new InvalidOperationException($"Parent not found: {parentIdOrPath}");
            parentId = parent.Id;
        }
        var go = CreateGameObjectInternal(name, parentId);
        if (!string.IsNullOrWhiteSpace(primitiveType))
        {
            AddComponent(go.Id, "MeshFilter");
            AddComponent(go.Id, "MeshRenderer");
            if (primitiveType.Equals("Cube", StringComparison.OrdinalIgnoreCase) ||
                primitiveType.Equals("Sphere", StringComparison.OrdinalIgnoreCase) ||
                primitiveType.Equals("Capsule", StringComparison.OrdinalIgnoreCase) ||
                primitiveType.Equals("Plane", StringComparison.OrdinalIgnoreCase) ||
                primitiveType.Equals("Quad", StringComparison.OrdinalIgnoreCase) ||
                primitiveType.Equals("Cylinder", StringComparison.OrdinalIgnoreCase))
            {
                AddComponent(go.Id, "BoxCollider");
                ModifyComponent(go.Id, "MeshFilter", new Dictionary<string, object?> { ["mesh"] = primitiveType });
            }
        }
        return CloneGo(go);
    }

    public bool DeleteGameObject(string idOrPath)
    {
        var go = ResolveMutable(idOrPath);
        if (go == null) return false;
        DeleteRecursive(go.Id);
        return true;
    }

    public GameObjectData? DuplicateGameObject(string idOrPath, string? newName = null)
    {
        var src = ResolveMutable(idOrPath);
        if (src == null) return null;
        var copy = CreateGameObjectInternal(newName ?? src.Name + " (Clone)", src.ParentId);
        copy.Transform = src.Transform;
        copy.Active = src.Active;
        copy.Tag = src.Tag;
        copy.Layer = src.Layer;
        foreach (var c in src.Components)
        {
            copy.Components.Add(new ComponentData
            {
                TypeName = c.TypeName,
                Properties = new Dictionary<string, object?>(c.Properties, StringComparer.OrdinalIgnoreCase)
            });
        }
        _active.Dirty = true;
        return CloneGo(copy);
    }

    public bool SetParent(string idOrPath, string? newParentIdOrPath)
    {
        var go = ResolveMutable(idOrPath);
        if (go == null) return false;
        string? newParentId = null;
        if (!string.IsNullOrWhiteSpace(newParentIdOrPath))
        {
            var p = ResolveMutable(newParentIdOrPath!);
            if (p == null) return false;
            if (p.Id == go.Id) return false;
            newParentId = p.Id;
        }
        if (go.ParentId != null && Objects.TryGetValue(go.ParentId, out var oldParent))
            oldParent.ChildIds.Remove(go.Id);
        else
            _active.RootObjectIds.Remove(go.Id);

        go.ParentId = newParentId;
        if (newParentId != null)
            Objects[newParentId].ChildIds.Add(go.Id);
        else
            _active.RootObjectIds.Add(go.Id);
        _active.Dirty = true;
        return true;
    }

    public bool SetTransform(string idOrPath, Vector3? position = null, Vector3? rotation = null, Vector3? scale = null)
    {
        var go = ResolveMutable(idOrPath);
        if (go == null) return false;
        go.Transform = new TransformData(
            position ?? go.Transform.Position,
            rotation ?? go.Transform.RotationEuler,
            scale ?? go.Transform.Scale);
        _active.Dirty = true;
        return true;
    }

    public bool SetActive(string idOrPath, bool active)
    {
        var go = ResolveMutable(idOrPath);
        if (go == null) return false;
        go.Active = active;
        _active.Dirty = true;
        return true;
    }

    public bool RenameGameObject(string idOrPath, string newName)
    {
        var go = ResolveMutable(idOrPath);
        if (go == null) return false;
        go.Name = newName;
        _active.Dirty = true;
        return true;
    }

    public bool SetTag(string idOrPath, string tag)
    {
        var go = ResolveMutable(idOrPath);
        if (go == null) return false;
        go.Tag = tag;
        _active.Dirty = true;
        return true;
    }

    public bool SetLayer(string idOrPath, int layer)
    {
        var go = ResolveMutable(idOrPath);
        if (go == null) return false;
        go.Layer = layer;
        _active.Dirty = true;
        return true;
    }

    // --- Components ---

    public bool AddComponent(string idOrPath, string typeName, Dictionary<string, object?>? properties = null)
    {
        var go = ResolveMutable(idOrPath);
        if (go == null) return false;
        if (go.Components.Any(c => c.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase)))
            return true;
        go.Components.Add(new ComponentData
        {
            TypeName = typeName,
            Properties = properties != null
                ? new Dictionary<string, object?>(properties, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        });
        _active.Dirty = true;
        return true;
    }

    public bool RemoveComponent(string idOrPath, string typeName)
    {
        var go = ResolveMutable(idOrPath);
        if (go == null) return false;
        var n = go.Components.RemoveAll(c => c.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (n > 0) _active.Dirty = true;
        return n > 0;
    }

    public bool ModifyComponent(string idOrPath, string typeName, Dictionary<string, object?> properties)
    {
        var go = ResolveMutable(idOrPath);
        if (go == null) return false;
        var comp = go.Components.FirstOrDefault(c => c.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (comp == null) return false;
        foreach (var kv in properties)
            comp.Properties[kv.Key] = kv.Value;
        _active.Dirty = true;
        return true;
    }

    public ComponentData? GetComponent(string idOrPath, string typeName)
    {
        var go = ResolveMutable(idOrPath);
        var comp = go?.Components.FirstOrDefault(c => c.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        if (comp == null) return null;
        return new ComponentData
        {
            TypeName = comp.TypeName,
            Properties = new Dictionary<string, object?>(comp.Properties, StringComparer.OrdinalIgnoreCase)
        };
    }

    public IReadOnlyList<string> ListComponentTypes(string? filter = null)
    {
        IEnumerable<string> q = BuiltinComponentTypes;
        if (!string.IsNullOrWhiteSpace(filter))
            q = q.Where(t => t.Contains(filter!, StringComparison.OrdinalIgnoreCase));
        return q.OrderBy(t => t).ToList();
    }

    // --- Assets ---

    public IReadOnlyList<AssetRecord> FindAssets(string? filter = null, string? kind = null)
    {
        var list = new List<AssetRecord>();
        foreach (var s in _scripts.Values)
            list.Add(new AssetRecord { Path = s.Path, Kind = "Script" });
        foreach (var m in _materials.Values)
            list.Add(new AssetRecord { Path = m.Path, Kind = "Material", MaterialColor = m.Color });
        foreach (var p in _prefabs.Values)
            list.Add(new AssetRecord { Path = p.Path, Kind = "Prefab", PrefabSourceObjectId = p.SourceObjectId });
        foreach (var f in _folders)
            list.Add(new AssetRecord { Path = f, Kind = "Folder" });

        if (!string.IsNullOrWhiteSpace(kind))
            list = list.Where(a => a.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var rx = WildcardToRegex(filter!);
            list = list.Where(a => rx.IsMatch(a.Path)).ToList();
        }
        return list;
    }

    public MaterialData CreateMaterial(string path, string? color = null, string? shader = null)
    {
        path = NormalizeAssetPath(path, ".mat");
        EnsureParentFolder(path);
        var mat = new MaterialData
        {
            Path = path,
            Name = Path.GetFileNameWithoutExtension(path),
            Color = color ?? "#FFFFFF",
            Shader = shader ?? "Standard"
        };
        _materials[path] = mat;
        return mat;
    }

    public bool AssignMaterial(string gameObjectIdOrPath, string materialPath)
    {
        materialPath = NormalizePath(materialPath);
        if (!_materials.ContainsKey(materialPath)) return false;
        var go = ResolveMutable(gameObjectIdOrPath);
        if (go == null) return false;
        var renderer = go.Components.FirstOrDefault(c =>
            c.TypeName.Equals("MeshRenderer", StringComparison.OrdinalIgnoreCase) ||
            c.TypeName.Equals("Renderer", StringComparison.OrdinalIgnoreCase));
        if (renderer == null)
        {
            renderer = new ComponentData { TypeName = "MeshRenderer" };
            go.Components.Add(renderer);
        }
        renderer.Properties["sharedMaterial"] = materialPath;
        _active.Dirty = true;
        return true;
    }

    public PrefabData CreatePrefab(string path, string sourceObjectIdOrPath)
    {
        var src = ResolveMutable(sourceObjectIdOrPath) ?? throw new InvalidOperationException("Source GO not found");
        path = NormalizeAssetPath(path, ".prefab");
        EnsureParentFolder(path);
        var prefab = new PrefabData
        {
            Path = path,
            Name = Path.GetFileNameWithoutExtension(path),
            SourceObjectId = src.Id
        };
        _prefabs[path] = prefab;
        _prefabTemplates[path] = CloneGo(src);
        return prefab;
    }

    public GameObjectData? InstantiatePrefab(string prefabPath, string? parentIdOrPath = null)
    {
        prefabPath = NormalizePath(prefabPath);
        if (!_prefabTemplates.TryGetValue(prefabPath, out var src)) return null;
        string? parentId = null;
        if (!string.IsNullOrWhiteSpace(parentIdOrPath))
            parentId = ResolveMutable(parentIdOrPath!)?.Id;
        var instance = CreateGameObjectInternal(src.Name, parentId);
        instance.Transform = src.Transform;
        instance.Tag = src.Tag;
        instance.Layer = src.Layer;
        instance.Components.Clear();
        foreach (var c in src.Components)
        {
            instance.Components.Add(new ComponentData
            {
                TypeName = c.TypeName,
                Properties = new Dictionary<string, object?>(c.Properties, StringComparer.OrdinalIgnoreCase)
            });
        }
        return CloneGo(instance);
    }

    public bool CreateFolder(string path)
    {
        path = NormalizePath(path).TrimEnd('/');
        if (!path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            path = "Assets/" + path.TrimStart('/');
        _folders.Add(path);
        // Ensure parents
        var parts = path.Split('/');
        var acc = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            acc += "/" + parts[i];
            _folders.Add(acc);
        }
        return true;
    }

    public bool DeleteAsset(string path)
    {
        path = NormalizePath(path);
        if (_scripts.Remove(path)) return true;
        if (_materials.Remove(path)) return true;
        if (_prefabs.Remove(path))
        {
            _prefabTemplates.Remove(path);
            return true;
        }
        if (_folders.Remove(path)) return true;
        return false;
    }

    public bool CopyAsset(string fromPath, string toPath)
    {
        fromPath = NormalizePath(fromPath);
        toPath = NormalizePath(toPath);
        if (_scripts.TryGetValue(fromPath, out var script))
        {
            WriteScript(toPath, script.Content);
            return true;
        }
        if (_materials.TryGetValue(fromPath, out var mat))
        {
            CreateMaterial(toPath, mat.Color, mat.Shader);
            return true;
        }
        if (_prefabs.TryGetValue(fromPath, out _) && _prefabTemplates.TryGetValue(fromPath, out var tmpl))
        {
            toPath = NormalizeAssetPath(toPath, ".prefab");
            _prefabs[toPath] = new PrefabData { Path = toPath, Name = Path.GetFileNameWithoutExtension(toPath), SourceObjectId = tmpl.Id };
            _prefabTemplates[toPath] = CloneGo(tmpl);
            return true;
        }
        return false;
    }

    public bool MoveAsset(string fromPath, string toPath)
    {
        if (!CopyAsset(fromPath, toPath)) return false;
        DeleteAsset(fromPath);
        return true;
    }

    public void RefreshAssets() =>
        AddConsoleLog(new ConsoleLogEntry(LogType.Log, "AssetDatabase refreshed (headless)."));

    public IReadOnlyList<string> ListShaders() => BuiltinShaders.ToList();

    // --- Packages ---

    public IReadOnlyList<PackageInfo> ListPackages() =>
        _packages.Select(p => new PackageInfo { Name = p.Name, Version = p.Version, Source = p.Source, DisplayName = p.DisplayName }).ToList();

    public PackageInfo AddPackage(string packageIdOrUrl)
    {
        var name = packageIdOrUrl.Contains("://") || packageIdOrUrl.Contains("git")
            ? packageIdOrUrl.Split('/').Last().Replace(".git", "")
            : packageIdOrUrl.Split('@')[0];
        var version = packageIdOrUrl.Contains('@') ? packageIdOrUrl.Split('@')[1] : "1.0.0";
        var source = packageIdOrUrl.Contains("git") || packageIdOrUrl.Contains("://") ? "git" : "registry";
        var existing = _packages.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Version = version;
            return new PackageInfo { Name = existing.Name, Version = existing.Version, Source = existing.Source, DisplayName = existing.DisplayName };
        }
        var pkg = new PackageInfo { Name = name, Version = version, Source = source, DisplayName = name };
        _packages.Add(pkg);
        AddConsoleLog(new ConsoleLogEntry(LogType.Log, $"Package added: {name}@{version}"));
        return pkg;
    }

    public bool RemovePackage(string packageName)
    {
        var n = _packages.RemoveAll(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
        return n > 0;
    }

    private static readonly PackageInfo[] RegistryCatalog =
    {
        new() { Name = "com.unity.cinemachine", Version = "2.9.7", Source = "registry", DisplayName = "Cinemachine" },
        new() { Name = "com.unity.addressables", Version = "1.21.0", Source = "registry", DisplayName = "Addressables" },
        new() { Name = "com.unity.netcode.gameobjects", Version = "1.8.0", Source = "registry", DisplayName = "Netcode" },
        new() { Name = "com.unity.timeline", Version = "1.7.0", Source = "registry", DisplayName = "Timeline" },
        new() { Name = "com.unity.probuilder", Version = "5.2.0", Source = "registry", DisplayName = "ProBuilder" },
    };

    public IReadOnlyList<PackageInfo> SearchPackages(string query)
    {
        query ??= "";
        bool Match(PackageInfo p) =>
            p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (p.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

        var installed = _packages.Where(Match);
        var catalog = RegistryCatalog.Where(Match)
            .Where(c => !_packages.Any(i => i.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase)));
        return installed.Concat(catalog)
            .Select(p => new PackageInfo { Name = p.Name, Version = p.Version, Source = p.Source, DisplayName = p.DisplayName })
            .ToList();
    }

    // --- Menu ---

    public IReadOnlyList<MenuItemInfo> ListMenuItems(string? filter = null)
    {
        IEnumerable<MenuItemInfo> q = _menuItems;
        if (!string.IsNullOrWhiteSpace(filter))
            q = q.Where(m => m.Path.Contains(filter!, StringComparison.OrdinalIgnoreCase));
        return q.ToList();
    }

    public bool ExecuteMenuItem(string menuPath)
    {
        var item = _menuItems.FirstOrDefault(m => m.Path.Equals(menuPath, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            // Allow unknown menu paths for extensibility; log only
            AddConsoleLog(new ConsoleLogEntry(LogType.Warning, $"Menu item not in catalog (executed anyway): {menuPath}"));
            return true;
        }

        // Simulate common menu side-effects
        if (menuPath.Equals("GameObject/Create Empty", StringComparison.OrdinalIgnoreCase))
            CreateGameObject("GameObject");
        else if (menuPath.Contains("Cube", StringComparison.OrdinalIgnoreCase))
            CreateGameObject("Cube", primitiveType: "Cube");
        else if (menuPath.Contains("Sphere", StringComparison.OrdinalIgnoreCase))
            CreateGameObject("Sphere", primitiveType: "Sphere");
        else if (menuPath.Contains("Plane", StringComparison.OrdinalIgnoreCase))
            CreateGameObject("Plane", primitiveType: "Plane");
        else if (menuPath.Contains("Directional Light", StringComparison.OrdinalIgnoreCase))
        {
            var l = CreateGameObject("Directional Light");
            AddComponent(l.Id, "Light");
        }
        else if (menuPath.Equals("GameObject/Camera", StringComparison.OrdinalIgnoreCase))
        {
            var c = CreateGameObject("Camera");
            AddComponent(c.Id, "Camera");
        }
        else if (menuPath.Equals("File/Save", StringComparison.OrdinalIgnoreCase))
            SaveScene();

        AddConsoleLog(new ConsoleLogEntry(LogType.Log, $"Menu executed: {menuPath}"));
        return true;
    }

    // --- Screenshots ---

    /// <summary>
    /// Test hook to inject a real PNG fixture for MCP image-protocol coverage.
    /// Production/headless path leaves this null (honest blindness).
    /// </summary>
    public Func<string, string?, int, int, int, int?, int?, int?, int?, ScreenshotResult>? ScreenshotOverride { get; set; }

    /// <summary>
    /// Headless cannot see. Returns explicit blindness (<see cref="ScreenshotResult.IsRealPixels"/> = false)
    /// unless <see cref="ScreenshotOverride"/> supplies a fixture.
    /// </summary>
    public ScreenshotResult CaptureScreenshot(
        string source,
        string? targetId = null,
        int width = 1280,
        int height = 720,
        int maxResolution = 640,
        int? regionX = null,
        int? regionY = null,
        int? regionWidth = null,
        int? regionHeight = null)
    {
        if (ScreenshotOverride != null)
            return ScreenshotOverride(source, targetId, width, height, maxResolution, regionX, regionY, regionWidth, regionHeight);

        return new ScreenshotResult
        {
            Source = source,
            TargetId = targetId,
            Width = width,
            Height = height,
            IsRealPixels = false,
            PngBase64 = null,
            FilePath = null,
            OverlayUiIncluded = null,
            Format = "none",
            Note =
                "Headless InMemoryEditorHost cannot capture real pixels. " +
                "Open a live Unity Editor with the Unity-Comdr bridge (UNITYCOMDR_LIVE=1) " +
                "and retry screenshot_capture with a camera or Scene View available."
        };
    }

    // --- P1 interaction / lease (pre-existing interface surface; required for compile) ---

    public IReadOnlyList<UiControlInfo> QueryUi(string? filter = null)
    {
        IEnumerable<UiControlInfo> q = _uiControls.Values;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            q = q.Where(c =>
                c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (c.Path?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                c.Id.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        return q.ToList();
    }

    public InputSimulateResult SimulateInput(
        string action,
        string? target = null,
        float? x = null,
        float? y = null,
        float? toX = null,
        float? toY = null,
        float? deltaX = null,
        float? deltaY = null,
        string? key = null)
    {
        var note = $"simulated {action}" +
                   (target != null ? $" target={target}" : "") +
                   (key != null ? $" key={key}" : "");
        _inputLog.Add(note);
        AddConsoleLog(new ConsoleLogEntry(LogType.Log, note));
        return new InputSimulateResult
        {
            Ok = true,
            Action = action,
            Target = target,
            Note = note,
            Effects =
            {
                ["x"] = x,
                ["y"] = y,
                ["toX"] = toX,
                ["toY"] = toY,
                ["deltaX"] = deltaX,
                ["deltaY"] = deltaY,
                ["key"] = key
            }
        };
    }

    public LeaseInfo GetLease() => _lease.GetLease();

    public LeaseInfo AcquireLease(string agentId, double ttlSeconds) =>
        _lease.Acquire(agentId, ttlSeconds);

    public bool ReleaseLease(string agentId) =>
        _lease.Release(agentId, out _);

    public LeaseAuthorization AuthorizeWrite(string? agentId, bool requireHeld = false) =>
        _lease.AuthorizeWrite(agentId, requireHeld);

    // --- Profiler ---

    public ProfilerSnapshot GetProfilerSnapshot()
    {
        if (_profiler.Enabled)
        {
            // Simulate sampling while profiler is on (production host path — non-empty real metrics).
            _profiler.DeltaTimeMs = 16.67f + (_frame % 3);
            _profiler.Fps = 1000f / Math.Max(0.01f, _profiler.DeltaTimeMs);
            _profiler.MonoUsedBytes = 32_000_000 + _frame * 1024;
            _profiler.TotalAllocatedBytes = 128_000_000 + _frame * 4096;
        }
        return new ProfilerSnapshot
        {
            Enabled = _profiler.Enabled,
            DeltaTimeMs = _profiler.DeltaTimeMs > 0 ? _profiler.DeltaTimeMs : 16.67f,
            Fps = _profiler.Fps > 0 ? _profiler.Fps : 60f,
            MonoUsedBytes = _profiler.MonoUsedBytes > 0 ? _profiler.MonoUsedBytes : 32_000_000,
            TotalAllocatedBytes = _profiler.TotalAllocatedBytes > 0 ? _profiler.TotalAllocatedBytes : 128_000_000,
            EnabledModules = _profiler.EnabledModules.Count > 0
                ? _profiler.EnabledModules.ToList()
                : new List<string> { "CPU", "Memory", "Rendering" }
        };
    }

    public void SetProfilerEnabled(bool enabled)
    {
        _profiler.Enabled = enabled;
        if (enabled)
        {
            if (_profiler.EnabledModules.Count == 0)
                _profiler.EnabledModules = new List<string> { "CPU", "Memory", "Rendering" };
            _profiler.DeltaTimeMs = 16.67f;
            _profiler.Fps = 60f;
            _profiler.MonoUsedBytes = 32_000_000;
            _profiler.TotalAllocatedBytes = 128_000_000;
        }
    }

    public void ClearProfilerData()
    {
        _profiler.DeltaTimeMs = 0;
        _profiler.Fps = 0;
        _profiler.MonoUsedBytes = 0;
        _profiler.TotalAllocatedBytes = 0;
        _frame = 0;
    }

    public void SaveProfilerData(string path)
    {
        path = NormalizePath(path);
        _profilerSaves[path] = GetProfilerSnapshot();
        AddConsoleLog(new ConsoleLogEntry(LogType.Log, $"Profiler snapshot saved: {path}"));
    }

    public ProfilerSnapshot? LoadProfilerData(string path)
    {
        path = NormalizePath(path);
        return _profilerSaves.TryGetValue(path, out var s) ? s : null;
    }

    // --- internals ---

    private GameObjectData CreateGameObjectInternal(string name, string? parentId)
    {
        var go = new GameObjectData { Name = name, ParentId = parentId };
        go.Components.Add(new ComponentData { TypeName = "Transform" });
        Objects[go.Id] = go;
        if (parentId != null && Objects.TryGetValue(parentId, out var parent))
            parent.ChildIds.Add(go.Id);
        else
            _active.RootObjectIds.Add(go.Id);
        _active.Dirty = true;
        return go;
    }

    private void DeleteRecursive(string id)
    {
        if (!Objects.TryGetValue(id, out var go)) return;
        foreach (var child in go.ChildIds.ToList())
            DeleteRecursive(child);
        if (go.ParentId != null && Objects.TryGetValue(go.ParentId, out var parent))
            parent.ChildIds.Remove(id);
        else
            _active.RootObjectIds.Remove(id);
        Objects.Remove(id);
        _selection.GameObjectIds.Remove(id);
        _active.Dirty = true;
    }

    private GameObjectData? ResolveMutable(string idOrPath)
    {
        ThrowIfStaleObjectId(idOrPath);
        if (Objects.TryGetValue(idOrPath, out var byId))
            return byId;
        var found = FindGameObject(idOrPath);
        return found == null ? null : Objects[found.Id];
    }

    private void ThrowIfStaleObjectId(string idOrPath)
    {
        if (string.IsNullOrWhiteSpace(idOrPath)) return;
        // Hierarchy paths are stable across reload; only bare ids are generation-scoped.
        if (idOrPath.Contains('/')) return;
        if (!_staleObjectIds.Contains(idOrPath)) return;
        throw new InvalidOperationException(
            $"stale_reference: GameObject id '{idOrPath}' is invalid after domain reload " +
            $"(sessionGeneration={_sessionGeneration}). Re-find by hierarchy path, then retry with the new id.");
    }

    private void RemintAllObjectIds()
    {
        var globalRemap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scenePath in _objectsByScene.Keys.ToList())
        {
            var oldMap = _objectsByScene[scenePath];
            var remap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var oldId in oldMap.Keys)
            {
                var newId = Guid.NewGuid().ToString("N")[..8];
                while (remap.ContainsValue(newId) || oldMap.ContainsKey(newId) || globalRemap.ContainsValue(newId))
                    newId = Guid.NewGuid().ToString("N")[..8];
                remap[oldId] = newId;
                globalRemap[oldId] = newId;
                _staleObjectIds.Add(oldId);
            }

            var next = new Dictionary<string, GameObjectData>(StringComparer.OrdinalIgnoreCase);
            foreach (var (oldId, go) in oldMap)
            {
                go.Id = remap[oldId];
                if (go.ParentId != null && remap.TryGetValue(go.ParentId, out var newParent))
                    go.ParentId = newParent;
                go.ChildIds = go.ChildIds.Select(c => remap.TryGetValue(c, out var nc) ? nc : c).ToList();
                next[go.Id] = go;
            }
            _objectsByScene[scenePath] = next;

            if (_scenes.TryGetValue(scenePath, out var scene))
            {
                scene.RootObjectIds = scene.RootObjectIds
                    .Select(id => remap.TryGetValue(id, out var nid) ? nid : id)
                    .ToList();
            }
        }

        _selection.GameObjectIds = _selection.GameObjectIds
            .Select(id => globalRemap.TryGetValue(id, out var nid) ? nid : id)
            .Where(id => !_staleObjectIds.Contains(id))
            .ToList();
    }

    private void EnsureParentFolder(string assetPath)
    {
        var dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(dir))
            CreateFolder(dir);
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim();

    private static string NormalizeAssetPath(string path, string extension)
    {
        path = NormalizePath(path);
        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            path += extension;
        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            path = "Assets/" + path.TrimStart('/');
        return path;
    }

    private static Regex WildcardToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".");
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static SceneData CloneScene(SceneData s) => new()
    {
        Path = s.Path,
        Name = s.Name,
        Dirty = s.Dirty,
        IsLoaded = s.IsLoaded,
        RootObjectIds = s.RootObjectIds.ToList()
    };

    private static GameObjectData CloneGo(GameObjectData g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        ParentId = g.ParentId,
        Active = g.Active,
        Tag = g.Tag,
        Layer = g.Layer,
        Transform = g.Transform,
        Components = g.Components.Select(c => new ComponentData
        {
            TypeName = c.TypeName,
            Properties = new Dictionary<string, object?>(c.Properties, StringComparer.OrdinalIgnoreCase)
        }).ToList(),
        ChildIds = g.ChildIds.ToList()
    };
}
