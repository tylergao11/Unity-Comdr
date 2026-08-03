using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using UnityComdr.Models;

namespace UnityComdr.Editor;

/// <summary>
/// Live <see cref="IEditorHost"/> that forwards every call to the Unity Editor TCP bridge.
/// Used when the Editor package bridge is running; same handlers as headless path.
/// </summary>
public sealed class BridgeClientEditorHost : IEditorHost, IDisposable
{
    private readonly int _port;
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly object _gate = new();
    private int _sessionGeneration;
    private bool _hasSessionGeneration;

    public BridgeClientEditorHost(int port = EditorHostFactory.DefaultLiveBridgePort)
    {
        _port = port;
    }

    /// <summary>Last sessionGeneration observed from ping/getState (O2).</summary>
    public int SessionGeneration => _sessionGeneration;

    public bool TryConnect(TimeSpan timeout)
    {
        try
        {
            var client = new TcpClient();
            var ar = client.BeginConnect("127.0.0.1", _port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeout))
            {
                try { client.Close(); } catch { /* ignore */ }
                return false;
            }
            client.EndConnect(ar);
            var stream = client.GetStream();
            stream.ReadTimeout = 15000;
            stream.WriteTimeout = 15000;
            _client = client;
            _reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            var pong = Call(BridgeProtocol.Methods.Ping, null, trackGeneration: true, allowGenerationChange: true);
            return pong.Ok;
        }
        catch
        {
            Dispose();
            return false;
        }
    }

    public void Dispose()
    {
        try { _writer?.Dispose(); } catch { /* ignore */ }
        try { _reader?.Dispose(); } catch { /* ignore */ }
        try { _client?.Dispose(); } catch { /* ignore */ }
        _writer = null;
        _reader = null;
        _client = null;
    }

    private BridgeProtocol.Response Call(
        string method,
        Dictionary<string, object?>? args,
        bool trackGeneration = true,
        bool allowGenerationChange = false)
    {
        lock (_gate)
        {
            if (_writer == null || _reader == null)
                throw new EditorBusyException(
                    EditorLifecyclePhases.EditorGone,
                    nextStep: "Live bridge not connected. Open Unity with Unity-Comdr package so the bridge listens, then retry.");

            try
            {
                var req = new BridgeProtocol.Request
                {
                    Method = method,
                    Args = args?.ToDictionary(
                        kv => kv.Key,
                        kv => JsonSerializer.SerializeToElement(kv.Value, BridgeProtocol.JsonOptions))
                };
                var line = JsonSerializer.Serialize(req, BridgeProtocol.JsonOptions);
                _writer.WriteLine(line);
                var responseLine = _reader.ReadLine();
                if (responseLine == null)
                {
                    Dispose();
                    throw new EditorBusyException(
                        EditorLifecyclePhases.EditorReloading,
                        nextStep: "Live bridge closed the connection (likely domain reload). Wait, reconnect, then retry.");
                }

                var resp = JsonSerializer.Deserialize<BridgeProtocol.Response>(responseLine, BridgeProtocol.JsonOptions)
                    ?? throw new InvalidOperationException("Invalid bridge response.");
                if (!resp.Ok)
                {
                    if (EditorBusyException.TryParse(resp.Error, out var busy) && busy != null)
                        throw busy;
                    throw new InvalidOperationException(resp.Error ?? "Bridge error");
                }

                if (trackGeneration)
                    NoteSessionGeneration(responseLine, allowGenerationChange);

                return resp;
            }
            catch (EditorBusyException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (IOException)
            {
                Dispose();
                throw new EditorBusyException(
                    EditorLifecyclePhases.EditorReloading,
                    nextStep: "Bridge I/O failed during Editor transition. Wait suggestedRetrySeconds, reconnect, then retry.");
            }
            catch (SocketException)
            {
                Dispose();
                throw new EditorBusyException(
                    EditorLifecyclePhases.EditorGone,
                    nextStep: "Bridge socket failed. Ensure Unity Editor live bridge is running, then retry.");
            }
        }
    }

    /// <summary>
    /// Best-effort O2 guard: if the bridge reports a new sessionGeneration mid-session,
    /// fail explicitly instead of continuing with stale instance ids.
    /// Lifecycle probes (ping/getState) may adopt the new generation without throwing.
    /// </summary>
    private void NoteSessionGeneration(string responseLine, bool allowGenerationChange)
    {
        var gen = TryReadIntProperty(responseLine, "sessionGeneration");
        if (gen is null) return;

        if (_hasSessionGeneration && gen.Value != _sessionGeneration && !allowGenerationChange)
        {
            var previous = _sessionGeneration;
            _sessionGeneration = gen.Value;
            throw new InvalidOperationException(
                $"stale_reference: sessionGeneration changed {previous} -> {gen.Value}. " +
                "Re-find GameObjects by hierarchy path; do not reuse prior instance ids.");
        }

        _sessionGeneration = gen.Value;
        _hasSessionGeneration = true;
    }

    private static int? TryReadIntProperty(string json, string key)
    {
        var marker = "\"" + key + "\":";
        var idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + marker.Length;
        while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
        var end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
        if (end > start && int.TryParse(json.AsSpan(start, end - start), out var value))
            return value;
        return null;
    }

    /// <summary>Id-based ops: refuse if sessionGeneration moved since last observed value.</summary>
    private void EnsureSessionStableForIdOp()
    {
        if (!_hasSessionGeneration) return;
        Call(BridgeProtocol.Methods.Ping, null, trackGeneration: true, allowGenerationChange: false);
    }

    private T Result<T>(BridgeProtocol.Response resp)
    {
        if (resp.Result is null || resp.Result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return default!;
        return resp.Result.Value.Deserialize<T>(BridgeProtocol.JsonOptions)!;
    }

    private static Dictionary<string, object?> A(params (string k, object? v)[] pairs)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    // --- Console ---
    public IReadOnlyList<ConsoleLogEntry> GetConsoleLogs() =>
        Result<List<ConsoleLogEntry>>(Call(BridgeProtocol.Methods.GetConsoleLogs, null)) ?? new();

    public void ClearConsole() => Call(BridgeProtocol.Methods.ClearConsole, null);

    public void AddConsoleLog(ConsoleLogEntry entry) =>
        Call(BridgeProtocol.Methods.AddConsoleLog, A(("entry", entry)));

    // --- Editor ---
    public EditorState GetState()
    {
        try
        {
            // Lifecycle probe may adopt a new sessionGeneration after domain reload.
            var state = Result<EditorState>(
                Call(BridgeProtocol.Methods.GetState, null, trackGeneration: true, allowGenerationChange: true)) ?? new();
            if (string.IsNullOrWhiteSpace(state.Phase))
            {
                state.Phase = state.IsCompiling
                    ? EditorLifecyclePhases.EditorCompiling
                    : EditorLifecyclePhases.Connected;
            }

            if (EditorLifecyclePhases.IsBusy(state.Phase) && state.SuggestedRetrySeconds is null)
                state.SuggestedRetrySeconds = EditorLifecyclePhases.DefaultRetrySeconds(state.Phase);
            if (state.SessionGeneration == 0 && _hasSessionGeneration)
                state.SessionGeneration = _sessionGeneration;
            state.HostMode = "live";
            state.HostDetail ??= $"BridgeClientEditorHost on 127.0.0.1:{_port}";
            return state;
        }
        catch (EditorBusyException busy)
        {
            return new EditorState
            {
                Phase = busy.Phase,
                SuggestedRetrySeconds = busy.SuggestedRetrySeconds,
                IsCompiling = string.Equals(busy.Phase, EditorLifecyclePhases.EditorCompiling, StringComparison.OrdinalIgnoreCase),
                ActiveScenePath = "",
                SessionGeneration = _sessionGeneration,
                HostMode = "live",
                HostDetail = $"BridgeClientEditorHost busy on 127.0.0.1:{_port}: {busy.Phase}"
            };
        }
    }

    public void SetCompiling(bool compiling) { /* Unity owns compile state */ }

    public void RequestScriptCompile() => Call(BridgeProtocol.Methods.RequestCompile, null);

    public void SetPlayMode(bool playing, bool paused = false) =>
        Call(BridgeProtocol.Methods.SetPlayMode, A(("playing", playing), ("paused", paused)));

    public void StepPlayModeFrame() => Call(BridgeProtocol.Methods.StepPlayMode, null);

    public SelectionState GetSelection() =>
        Result<SelectionState>(Call(BridgeProtocol.Methods.SelectionGet, null)) ?? new();

    public void SetSelection(IReadOnlyList<string>? gameObjectIds = null, IReadOnlyList<string>? assetPaths = null) =>
        Call(BridgeProtocol.Methods.SelectionSet, A(("gameObjectIds", gameObjectIds), ("assetPaths", assetPaths)));

    // --- Scripts ---
    public IReadOnlyList<string> ListScripts(string? underPath = null) =>
        Result<List<string>>(Call(BridgeProtocol.Methods.ScriptList, A(("underPath", underPath)))) ?? new();

    public string? ReadScript(string path) =>
        Result<string?>(Call(BridgeProtocol.Methods.ScriptRead, A(("path", path))));

    public void WriteScript(string path, string content) =>
        Call(BridgeProtocol.Methods.ScriptWrite, A(("path", path), ("content", content)));

    public bool DeleteScript(string path) =>
        Result<bool>(Call(BridgeProtocol.Methods.ScriptDelete, A(("path", path))));

    // --- Scenes ---
    public SceneData GetActiveScene() =>
        Result<SceneData>(Call(BridgeProtocol.Methods.SceneGet, null)) ?? new();

    public IReadOnlyList<SceneData> ListScenes() =>
        Result<List<SceneData>>(Call(BridgeProtocol.Methods.SceneList, null)) ?? new();

    public IReadOnlyList<SceneData> ListOpenedScenes() =>
        Result<List<SceneData>>(Call(BridgeProtocol.Methods.SceneListOpened, null)) ?? new();

    public SceneData CreateScene(string path, string? name = null) =>
        Result<SceneData>(Call(BridgeProtocol.Methods.SceneCreate, A(("path", path), ("name", name)))) ?? new();

    public SceneData OpenScene(string path, bool additive = false) =>
        Result<SceneData>(Call(BridgeProtocol.Methods.SceneOpen, A(("path", path), ("additive", additive)))) ?? new();

    public void SaveScene(string? path = null) =>
        Call(BridgeProtocol.Methods.SceneSave, A(("path", path)));

    public bool UnloadScene(string path) =>
        Result<bool>(Call(BridgeProtocol.Methods.SceneUnload, A(("path", path))));

    public bool SetActiveScene(string path) =>
        Result<bool>(Call(BridgeProtocol.Methods.SceneSetActive, A(("path", path))));

    // --- GameObjects ---
    public GameObjectData? FindGameObject(string idOrPath)
    {
        // Path-based lookup remains valid across reload; bare ids need a generation check.
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<GameObjectData?>(Call(BridgeProtocol.Methods.GoFind, A(("idOrPath", idOrPath))));
    }

    public IReadOnlyList<GameObjectData> FindGameObjects(string? name = null, string? tag = null, string? componentType = null) =>
        Result<List<GameObjectData>>(Call(BridgeProtocol.Methods.GoFindMany, A(("name", name), ("tag", tag), ("componentType", componentType)))) ?? new();

    public IReadOnlyList<GameObjectData> GetAllGameObjects() =>
        Result<List<GameObjectData>>(Call(BridgeProtocol.Methods.GoAll, null)) ?? new();

    public GameObjectData CreateGameObject(string name, string? parentIdOrPath = null, string? primitiveType = null) =>
        Result<GameObjectData>(Call(BridgeProtocol.Methods.GoCreate, A(("name", name), ("parent", parentIdOrPath), ("primitive", primitiveType)))) ?? new();

    public bool DeleteGameObject(string idOrPath)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<bool>(Call(BridgeProtocol.Methods.GoDelete, A(("idOrPath", idOrPath))));
    }

    public GameObjectData? DuplicateGameObject(string idOrPath, string? newName = null)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<GameObjectData?>(Call(BridgeProtocol.Methods.GoDuplicate, A(("idOrPath", idOrPath), ("newName", newName))));
    }

    public bool SetParent(string idOrPath, string? newParentIdOrPath)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<bool>(Call(BridgeProtocol.Methods.GoSetParent, A(("idOrPath", idOrPath), ("parent", newParentIdOrPath))));
    }

    public bool SetTransform(string idOrPath, Vector3? position = null, Vector3? rotation = null, Vector3? scale = null)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<bool>(Call(BridgeProtocol.Methods.GoSetTransform, A(("idOrPath", idOrPath), ("position", position), ("rotation", rotation), ("scale", scale))));
    }

    public bool SetActive(string idOrPath, bool active)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<bool>(Call(BridgeProtocol.Methods.GoSetActive, A(("idOrPath", idOrPath), ("active", active))));
    }

    public bool RenameGameObject(string idOrPath, string newName)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<bool>(Call(BridgeProtocol.Methods.GoRename, A(("idOrPath", idOrPath), ("newName", newName))));
    }

    public bool SetTag(string idOrPath, string tag)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<bool>(Call(BridgeProtocol.Methods.GoSetTag, A(("idOrPath", idOrPath), ("tag", tag))));
    }

    public bool SetLayer(string idOrPath, int layer)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<bool>(Call(BridgeProtocol.Methods.GoSetLayer, A(("idOrPath", idOrPath), ("layer", layer))));
    }

    // --- Components ---
    public bool AddComponent(string idOrPath, string typeName, Dictionary<string, object?>? properties = null)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<bool>(Call(BridgeProtocol.Methods.CompAdd, A(("idOrPath", idOrPath), ("typeName", typeName), ("properties", properties))));
    }

    public bool RemoveComponent(string idOrPath, string typeName)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<bool>(Call(BridgeProtocol.Methods.CompRemove, A(("idOrPath", idOrPath), ("typeName", typeName))));
    }

    public bool ModifyComponent(string idOrPath, string typeName, Dictionary<string, object?> properties)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<bool>(Call(BridgeProtocol.Methods.CompModify, A(("idOrPath", idOrPath), ("typeName", typeName), ("properties", properties))));
    }

    public ComponentData? GetComponent(string idOrPath, string typeName)
    {
        if (!string.IsNullOrEmpty(idOrPath) && !idOrPath.Contains('/'))
            EnsureSessionStableForIdOp();
        return Result<ComponentData?>(Call(BridgeProtocol.Methods.CompGet, A(("idOrPath", idOrPath), ("typeName", typeName))));
    }

    public IReadOnlyList<string> ListComponentTypes(string? filter = null) =>
        Result<List<string>>(Call(BridgeProtocol.Methods.CompListTypes, A(("filter", filter)))) ?? new();

    // --- Assets ---
    public IReadOnlyList<AssetRecord> FindAssets(string? filter = null, string? kind = null) =>
        Result<List<AssetRecord>>(Call(BridgeProtocol.Methods.AssetsFind, A(("filter", filter), ("kind", kind)))) ?? new();

    public MaterialData CreateMaterial(string path, string? color = null, string? shader = null) =>
        Result<MaterialData>(Call(BridgeProtocol.Methods.MaterialCreate, A(("path", path), ("color", color), ("shader", shader)))) ?? new();

    public bool AssignMaterial(string gameObjectIdOrPath, string materialPath) =>
        Result<bool>(Call(BridgeProtocol.Methods.MaterialAssign, A(("target", gameObjectIdOrPath), ("path", materialPath))));

    public PrefabData CreatePrefab(string path, string sourceObjectIdOrPath) =>
        Result<PrefabData>(Call(BridgeProtocol.Methods.PrefabCreate, A(("path", path), ("source", sourceObjectIdOrPath)))) ?? new();

    public GameObjectData? InstantiatePrefab(string prefabPath, string? parentIdOrPath = null) =>
        Result<GameObjectData?>(Call(BridgeProtocol.Methods.PrefabInstantiate, A(("path", prefabPath), ("parent", parentIdOrPath))));

    public bool CreateFolder(string path) =>
        Result<bool>(Call(BridgeProtocol.Methods.FolderCreate, A(("path", path))));

    public bool DeleteAsset(string path) =>
        Result<bool>(Call(BridgeProtocol.Methods.AssetDelete, A(("path", path))));

    public bool CopyAsset(string fromPath, string toPath) =>
        Result<bool>(Call(BridgeProtocol.Methods.AssetCopy, A(("fromPath", fromPath), ("toPath", toPath))));

    public bool MoveAsset(string fromPath, string toPath) =>
        Result<bool>(Call(BridgeProtocol.Methods.AssetMove, A(("fromPath", fromPath), ("toPath", toPath))));

    public void RefreshAssets() => Call(BridgeProtocol.Methods.AssetsRefresh, null);

    public IReadOnlyList<string> ListShaders() =>
        Result<List<string>>(Call(BridgeProtocol.Methods.ShaderList, null)) ?? new();

    // --- Packages / menu / screenshot / profiler ---
    public IReadOnlyList<PackageInfo> ListPackages() =>
        Result<List<PackageInfo>>(Call(BridgeProtocol.Methods.PackageList, null)) ?? new();

    public PackageInfo AddPackage(string packageIdOrUrl) =>
        Result<PackageInfo>(Call(BridgeProtocol.Methods.PackageAdd, A(("package", packageIdOrUrl)))) ?? new();

    public bool RemovePackage(string packageName) =>
        Result<bool>(Call(BridgeProtocol.Methods.PackageRemove, A(("package", packageName))));

    public IReadOnlyList<PackageInfo> SearchPackages(string query) =>
        Result<List<PackageInfo>>(Call(BridgeProtocol.Methods.PackageSearch, A(("query", query)))) ?? new();

    public IReadOnlyList<MenuItemInfo> ListMenuItems(string? filter = null) =>
        Result<List<MenuItemInfo>>(Call(BridgeProtocol.Methods.MenuList, A(("filter", filter)))) ?? new();

    public bool ExecuteMenuItem(string menuPath) =>
        Result<bool>(Call(BridgeProtocol.Methods.MenuExecute, A(("path", menuPath))));

    public ScreenshotResult CaptureScreenshot(
        string source,
        string? targetId = null,
        int width = 1280,
        int height = 720,
        int maxResolution = 640,
        int? regionX = null,
        int? regionY = null,
        int? regionWidth = null,
        int? regionHeight = null) =>
        Result<ScreenshotResult>(Call(BridgeProtocol.Methods.Screenshot, A(
            ("source", source),
            ("targetId", targetId),
            ("width", width),
            ("height", height),
            ("maxResolution", maxResolution),
            ("regionX", regionX),
            ("regionY", regionY),
            ("regionWidth", regionWidth),
            ("regionHeight", regionHeight))))
        ?? new ScreenshotResult
        {
            Source = source,
            TargetId = targetId,
            Width = width,
            Height = height,
            IsRealPixels = false,
            Format = "none",
            Note = "Live bridge returned an empty screenshot result."
        };

    public ProfilerSnapshot GetProfilerSnapshot() =>
        Result<ProfilerSnapshot>(Call(BridgeProtocol.Methods.ProfilerGet, null)) ?? new();

    public void SetProfilerEnabled(bool enabled) =>
        Call(BridgeProtocol.Methods.ProfilerSetEnabled, A(("enabled", enabled)));

    public void ClearProfilerData() => Call(BridgeProtocol.Methods.ProfilerClear, null);

    public void SaveProfilerData(string path) =>
        Call(BridgeProtocol.Methods.ProfilerSave, A(("path", path)));

    public ProfilerSnapshot? LoadProfilerData(string path) =>
        Result<ProfilerSnapshot?>(Call(BridgeProtocol.Methods.ProfilerLoad, A(("path", path))));

    public IReadOnlyList<UiControlInfo> QueryUi(string? filter = null) =>
        Result<List<UiControlInfo>>(Call(BridgeProtocol.Methods.UiQuery, A(("filter", filter)))) ?? new();

    public InputSimulateResult SimulateInput(
        string action,
        string? target = null,
        float? x = null,
        float? y = null,
        float? toX = null,
        float? toY = null,
        float? deltaX = null,
        float? deltaY = null,
        string? key = null) =>
        Result<InputSimulateResult>(Call(BridgeProtocol.Methods.InputSimulate, A(
            ("action", action),
            ("target", target),
            ("x", x),
            ("y", y),
            ("toX", toX),
            ("toY", toY),
            ("deltaX", deltaX),
            ("deltaY", deltaY),
            ("key", key))))
        ?? new InputSimulateResult { Ok = false, Action = action, Note = "empty bridge result" };

    public LeaseInfo GetLease() =>
        Result<LeaseInfo>(Call(BridgeProtocol.Methods.LeaseGet, null)) ?? new();

    public LeaseInfo AcquireLease(string agentId, double ttlSeconds) =>
        Result<LeaseInfo>(Call(BridgeProtocol.Methods.LeaseAcquire, A(("agentId", agentId), ("ttlSeconds", ttlSeconds))))
        ?? new();

    public bool ReleaseLease(string agentId) =>
        Result<bool>(Call(BridgeProtocol.Methods.LeaseRelease, A(("agentId", agentId))));

    public LeaseAuthorization AuthorizeWrite(string? agentId, bool requireHeld = false)
    {
        var lease = GetLease();
        if (!lease.Held)
            return requireHeld ? LeaseAuthorization.MissingLease() : LeaseAuthorization.Ok();
        if (!string.IsNullOrWhiteSpace(agentId) &&
            string.Equals(lease.Holder, agentId, StringComparison.OrdinalIgnoreCase))
            return LeaseAuthorization.Ok(lease.Holder, lease.ExpiresAt);
        return LeaseAuthorization.Busy(lease.Holder, lease.ExpiresAt);
    }
}
