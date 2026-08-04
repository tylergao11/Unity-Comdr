using UnityComdr.Models;

namespace UnityComdr.Editor;
// UiControlInfo / Lease* / InputSimulateResult live in UnityComdr.Models

/// <summary>
/// Abstraction over Unity Editor APIs so handlers and the MCP host share one surface.
/// Production uses UnityEditorHost; tests and headless use InMemoryEditorHost.
/// Surface intentionally covers parity areas with Coplay / IvanMurzak / CoderGamester.
/// </summary>
public interface IEditorHost
{
    /// <summary>
    /// <c>live</c> = Unity Editor bridge; <c>headless</c> = InMemory synthetic (not a real project).
    /// </summary>
    string HostMode { get; }

    // Console
    IReadOnlyList<ConsoleLogEntry> GetConsoleLogs();
    void ClearConsole();
    void AddConsoleLog(ConsoleLogEntry entry);

    // Editor state / compile / play mode
    EditorState GetState();
    void SetCompiling(bool compiling);
    void RequestScriptCompile();
    void SetPlayMode(bool playing, bool paused = false);
    void StepPlayModeFrame();

    // Selection (CoderGamester / IvanMurzak parity)
    SelectionState GetSelection();
    void SetSelection(IReadOnlyList<string>? gameObjectIds = null, IReadOnlyList<string>? assetPaths = null);

    // Scripts (project Assets)
    IReadOnlyList<string> ListScripts(string? underPath = null);
    string? ReadScript(string path);
    void WriteScript(string path, string content);
    bool DeleteScript(string path);

    // Scenes
    SceneData GetActiveScene();
    IReadOnlyList<SceneData> ListScenes();
    IReadOnlyList<SceneData> ListOpenedScenes();
    SceneData CreateScene(string path, string? name = null);
    SceneData OpenScene(string path, bool additive = false);
    void SaveScene(string? path = null);
    bool UnloadScene(string path);
    bool SetActiveScene(string path);

    // GameObjects
    GameObjectData? FindGameObject(string idOrPath);
    IReadOnlyList<GameObjectData> FindGameObjects(string? name = null, string? tag = null, string? componentType = null);
    IReadOnlyList<GameObjectData> GetAllGameObjects();
    GameObjectData CreateGameObject(string name, string? parentIdOrPath = null, string? primitiveType = null);
    bool DeleteGameObject(string idOrPath);
    GameObjectData? DuplicateGameObject(string idOrPath, string? newName = null);
    bool SetParent(string idOrPath, string? newParentIdOrPath);
    bool SetTransform(string idOrPath, Vector3? position = null, Vector3? rotation = null, Vector3? scale = null);
    bool SetActive(string idOrPath, bool active);
    bool RenameGameObject(string idOrPath, string newName);
    bool SetTag(string idOrPath, string tag);
    bool SetLayer(string idOrPath, int layer);

    // Components
    bool AddComponent(string idOrPath, string typeName, Dictionary<string, object?>? properties = null);
    bool RemoveComponent(string idOrPath, string typeName);
    bool ModifyComponent(string idOrPath, string typeName, Dictionary<string, object?> properties);
    ComponentData? GetComponent(string idOrPath, string typeName);
    IReadOnlyList<string> ListComponentTypes(string? filter = null);

    // Assets / materials / prefabs / folders
    IReadOnlyList<AssetRecord> FindAssets(string? filter = null, string? kind = null);
    MaterialData CreateMaterial(string path, string? color = null, string? shader = null);
    bool AssignMaterial(string gameObjectIdOrPath, string materialPath);
    PrefabData CreatePrefab(string path, string sourceObjectIdOrPath);
    GameObjectData? InstantiatePrefab(string prefabPath, string? parentIdOrPath = null);
    bool CreateFolder(string path);
    bool DeleteAsset(string path);
    bool CopyAsset(string fromPath, string toPath);
    bool MoveAsset(string fromPath, string toPath);
    void RefreshAssets();
    IReadOnlyList<string> ListShaders();

    // Package Manager (IvanMurzak / CoderGamester parity) — live uses PackageManager.Client
    IReadOnlyList<PackageInfo> ListPackages();
    PackageInfo AddPackage(string packageIdOrUrl);
    bool RemovePackage(string packageName);
    IReadOnlyList<PackageInfo> SearchPackages(string query);

    // Unity Test Runner (live TestRunnerApi only; headless returns Status=unsupported)
    TestJobSnapshot StartTests(string mode, string? filter = null);
    TestJobSnapshot GetTestJob(string jobId);
    IReadOnlyList<TestCatalogEntry> ListTests(string? mode = null);

    // Menu items (CoderGamester execute_menu_item parity)
    IReadOnlyList<MenuItemInfo> ListMenuItems(string? filter = null);
    bool ExecuteMenuItem(string menuPath);

    // Screenshots — live returns real pixels; headless returns IsRealPixels=false (honest blindness).
    ScreenshotResult CaptureScreenshot(
        string source,
        string? targetId = null,
        int width = 1280,
        int height = 720,
        int maxResolution = 640,
        int? regionX = null,
        int? regionY = null,
        int? regionWidth = null,
        int? regionHeight = null,
        string? batch = null);

    // Profiler (IvanMurzak parity — headless synthetic)
    ProfilerSnapshot GetProfilerSnapshot();
    void SetProfilerEnabled(bool enabled);
    void ClearProfilerData();
    void SaveProfilerData(string path);
    ProfilerSnapshot? LoadProfilerData(string path);

    // P1 interaction (TheHarness DESIGN §5.2 / §5.3 / §13.2)
    IReadOnlyList<UiControlInfo> QueryUi(string? filter = null);
    InputSimulateResult SimulateInput(
        string action,
        string? target = null,
        float? x = null,
        float? y = null,
        float? toX = null,
        float? toY = null,
        float? deltaX = null,
        float? deltaY = null,
        string? key = null);
    LeaseInfo GetLease();
    LeaseInfo AcquireLease(string agentId, double ttlSeconds);
    bool ReleaseLease(string agentId);
    LeaseAuthorization AuthorizeWrite(string? agentId, bool requireHeld = false);
}
