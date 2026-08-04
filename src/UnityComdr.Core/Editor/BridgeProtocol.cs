using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityComdr.Editor;

/// <summary>
/// JSON line protocol between UnityComdr.McpHost and the Unity Editor live bridge.
/// One request/response object per TCP line.
/// </summary>
public static class BridgeProtocol
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public sealed class Request
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Method { get; set; } = "";
        public Dictionary<string, JsonElement>? Args { get; set; }
    }

    public sealed class Response
    {
        public string Id { get; set; } = "";
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public JsonElement? Result { get; set; }
    }

    // Method names mirror IEditorHost operations used by full loops.
    public static class Methods
    {
        public const string Ping = "ping";
        public const string GetConsoleLogs = "console.get";
        public const string ClearConsole = "console.clear";
        public const string AddConsoleLog = "console.add";
        public const string GetState = "editor.getState";
        public const string RequestCompile = "editor.compile";
        public const string SetPlayMode = "editor.setPlayMode";
        public const string StepPlayMode = "editor.step";
        public const string ScriptRead = "script.read";
        public const string ScriptWrite = "script.write";
        public const string ScriptDelete = "script.delete";
        public const string ScriptList = "script.list";
        public const string SceneGet = "scene.get";
        public const string SceneList = "scene.list";
        public const string SceneListOpened = "scene.listOpened";
        public const string SceneCreate = "scene.create";
        public const string SceneOpen = "scene.open";
        public const string SceneSave = "scene.save";
        public const string SceneUnload = "scene.unload";
        public const string SceneSetActive = "scene.setActive";
        public const string GoFind = "go.find";
        public const string GoFindMany = "go.findMany";
        public const string GoAll = "go.all";
        public const string GoCreate = "go.create";
        public const string GoDelete = "go.delete";
        public const string GoDuplicate = "go.duplicate";
        public const string GoSetParent = "go.setParent";
        public const string GoSetTransform = "go.setTransform";
        public const string GoSetActive = "go.setActive";
        public const string GoRename = "go.rename";
        public const string GoSetTag = "go.setTag";
        public const string GoSetLayer = "go.setLayer";
        public const string CompAdd = "comp.add";
        public const string CompRemove = "comp.remove";
        public const string CompModify = "comp.modify";
        public const string CompGet = "comp.get";
        public const string CompListTypes = "comp.listTypes";
        public const string AssetsFind = "assets.find";
        public const string MaterialCreate = "assets.materialCreate";
        public const string MaterialAssign = "assets.materialAssign";
        public const string PrefabCreate = "assets.prefabCreate";
        public const string PrefabInstantiate = "assets.prefabInstantiate";
        public const string FolderCreate = "assets.folderCreate";
        public const string AssetDelete = "assets.delete";
        public const string AssetCopy = "assets.copy";
        public const string AssetMove = "assets.move";
        public const string AssetsRefresh = "assets.refresh";
        public const string ShaderList = "assets.listShaders";
        public const string SelectionGet = "selection.get";
        public const string SelectionSet = "selection.set";
        public const string PackageList = "package.list";
        public const string PackageAdd = "package.add";
        public const string PackageRemove = "package.remove";
        public const string PackageSearch = "package.search";
        /// <summary>Poll async UPM job started by package.list/add/remove/search.</summary>
        public const string PackageStatus = "package.status";
        public const string TestsRun = "tests.run";
        public const string TestsStatus = "tests.status";
        public const string TestsList = "tests.list";
        public const string MenuList = "menu.list";
        public const string MenuExecute = "menu.execute";
        public const string Screenshot = "screenshot.capture";
        public const string ProfilerGet = "profiler.get";
        public const string ProfilerSetEnabled = "profiler.setEnabled";
        public const string ProfilerClear = "profiler.clear";
        public const string ProfilerSave = "profiler.save";
        public const string ProfilerLoad = "profiler.load";
        // P1 interaction / lease
        public const string UiQuery = "ui.query";
        public const string InputSimulate = "input.simulate";
        public const string LeaseGet = "lease.get";
        public const string LeaseAcquire = "lease.acquire";
        public const string LeaseRelease = "lease.release";
    }
}
