#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace UnityComdr.UnityEditor
{
    /// <summary>
    /// Live Editor TCP bridge. MCP host (BridgeClientEditorHost) connects here so the same
    /// tool handlers drive real Unity Editor state when the Editor is open.
    /// Protocol: one JSON request/response line per message (see BridgeProtocol in Core).
    /// </summary>
    [InitializeOnLoad]
    public static class LiveUnityBridgeServer
    {
        public const int DefaultPort = 17890;
        public static bool IsRunning { get; private set; }
        public static string Status { get; private set; } = "Stopped";

        private static TcpListener _listener;
        private static CancellationTokenSource _cts;
        private static readonly object Gate = new object();
        private static readonly List<LogEntry> Logs = new List<LogEntry>();
        private static Thread _acceptThread;
        private static bool _profilerEnabled;
        private static readonly Dictionary<string, string> ProfilerSaves = new Dictionary<string, string>();
        private static readonly string[] BuiltinMenuCatalog =
        {
            "GameObject/Create Empty",
            "GameObject/3D Object/Cube",
            "GameObject/3D Object/Sphere",
            "GameObject/3D Object/Plane",
            "GameObject/Light/Directional Light",
            "GameObject/Camera",
            "Assets/Create/Folder",
            "Assets/Create/Material",
            "Assets/Create/C# Script",
            "Edit/Project Settings...",
            "Window/General/Console",
            "Window/General/Test Runner",
            "File/Save",
            "File/Save Project"
        };

        static LiveUnityBridgeServer()
        {
            Application.logMessageReceivedThreaded += OnLog;
            EditorApplication.delayCall += StartIfEnabled;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            lock (Gate)
            {
                Logs.Add(new LogEntry
                {
                    Type = type == LogType.Error || type == LogType.Exception ? "Error"
                        : type == LogType.Warning ? "Warning" : "Log",
                    Message = condition ?? "",
                    StackTrace = stackTrace,
                    File = null,
                    Line = 0
                });
                if (Logs.Count > 2000)
                    Logs.RemoveRange(0, Logs.Count - 1500);
            }
        }

        [MenuItem("Window/Unity-Comdr MCP/Start Live Bridge")]
        public static void StartMenu() => Start(DefaultPort);

        [MenuItem("Window/Unity-Comdr MCP/Stop Live Bridge")]
        public static void StopMenu() => Stop();

        private static void StartIfEnabled()
        {
            // Auto-start so full agent loop can attach without extra clicks.
            if (!IsRunning)
                Start(DefaultPort);
        }

        public static void Start(int port)
        {
            if (IsRunning) return;
            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                IsRunning = true;
                Status = $"Listening 127.0.0.1:{port}";
                _acceptThread = new Thread(() => AcceptLoop(_cts.Token)) { IsBackground = true, Name = "UnityComdrBridge" };
                _acceptThread.Start();
                Debug.Log($"[Unity-Comdr] Live bridge started on 127.0.0.1:{port}");
            }
            catch (Exception ex)
            {
                Status = "Failed: " + ex.Message;
                Debug.LogWarning("[Unity-Comdr] Live bridge failed to start: " + ex.Message);
                Stop();
            }
        }

        public static void Stop()
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _listener?.Stop(); } catch { /* ignore */ }
            IsRunning = false;
            Status = "Stopped";
            _listener = null;
            _cts = null;
        }

        private static void AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    if (!_listener.Pending())
                    {
                        Thread.Sleep(50);
                        continue;
                    }
                    var client = _listener.AcceptTcpClient();
                    Task.Run(() => HandleClient(client, ct), ct);
                }
                catch (SocketException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Unity-Comdr] accept error: " + ex.Message);
                }
            }
        }

        private static void HandleClient(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
            {
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    string line;
                    try { line = reader.ReadLine(); }
                    catch { break; }
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string responseJson = null;
                    var done = new ManualResetEventSlim(false);
                    // Unity API must run on main thread.
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            responseJson = Dispatch(line);
                        }
                        catch (Exception ex)
                        {
                            responseJson = Fail(null, ex.Message);
                        }
                        finally
                        {
                            done.Set();
                        }
                    };
                    if (!done.Wait(20000))
                        responseJson = Fail(null, "Editor main thread timeout");
                    try { writer.WriteLine(responseJson ?? Fail(null, "empty response")); }
                    catch { break; }
                }
            }
        }

        private static string Dispatch(string line)
        {
            // Minimal JSON parse without Core dependency (Unity package is standalone).
            var method = ExtractString(line, "method");
            var id = ExtractString(line, "id") ?? Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(method))
                return Fail(id, "missing method");

            try
            {
                switch (method)
                {
                    case "ping":
                        return Ok(id, "{\"pong\":true}");
                    case "console.get":
                        return Ok(id, SerializeLogs());
                    case "console.clear":
                        lock (Gate) Logs.Clear();
                        try { typeof(UnityEditor.LogEntries).GetMethod("Clear")?.Invoke(null, null); } catch { /* optional */ }
                        return Ok(id, "null");
                    case "console.add":
                    {
                        var msg = ExtractNestedString(line, "message") ?? ExtractString(line, "message") ?? "log";
                        var type = ExtractNestedString(line, "type") ?? "Log";
                        lock (Gate) Logs.Add(new LogEntry { Type = type, Message = msg });
                        return Ok(id, "null");
                    }
                    case "editor.getState":
                        return Ok(id, SerializeState());
                    case "editor.compile":
                        AssetDatabase.Refresh();
                        return Ok(id, "null");
                    case "editor.setPlayMode":
                    {
                        var playing = line.IndexOf("\"playing\":true", StringComparison.OrdinalIgnoreCase) >= 0
                                      || line.IndexOf("\"playing\": true", StringComparison.OrdinalIgnoreCase) >= 0;
                        var paused = line.IndexOf("\"paused\":true", StringComparison.OrdinalIgnoreCase) >= 0
                                     || line.IndexOf("\"paused\": true", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!playing)
                        {
                            EditorApplication.isPlaying = false;
                        }
                        else
                        {
                            EditorApplication.isPlaying = true;
                            EditorApplication.isPaused = paused;
                        }
                        return Ok(id, "null");
                    }
                    case "editor.step":
                        EditorApplication.Step();
                        return Ok(id, "null");
                    case "script.read":
                    {
                        var path = ExtractString(line, "path");
                        if (string.IsNullOrEmpty(path) || !File.Exists(ToFull(path)))
                            return Ok(id, "null");
                        var text = File.ReadAllText(ToFull(path));
                        return Ok(id, JsonString(text));
                    }
                    case "script.write":
                    {
                        var path = ExtractString(line, "path");
                        var content = ExtractString(line, "content") ?? "";
                        path = NormalizeAsset(path, ".cs");
                        var full = ToFull(path);
                        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
                        File.WriteAllText(full, content);
                        AssetDatabase.ImportAsset(path);
                        return Ok(id, "null");
                    }
                    case "script.delete":
                    {
                        var path = ExtractString(line, "path");
                        var ok = !string.IsNullOrEmpty(path) && AssetDatabase.DeleteAsset(path);
                        return Ok(id, ok ? "true" : "false");
                    }
                    case "script.list":
                    {
                        var under = ExtractString(line, "underPath") ?? "Assets";
                        var guids = AssetDatabase.FindAssets("t:Script", new[] { under });
                        var list = new List<string>();
                        foreach (var g in guids)
                            list.Add(AssetDatabase.GUIDToAssetPath(g));
                        return Ok(id, SerializeStringArray(list));
                    }
                    case "scene.get":
                        return Ok(id, SerializeScene(SceneManager.GetActiveScene()));
                    case "scene.list":
                    case "scene.listOpened":
                    {
                        var scenes = new List<string>();
                        for (var i = 0; i < SceneManager.sceneCount; i++)
                            scenes.Add(SerializeScene(SceneManager.GetSceneAt(i)));
                        return Ok(id, "[" + string.Join(",", scenes) + "]");
                    }
                    case "scene.create":
                    {
                        var path = NormalizeAsset(ExtractString(line, "path"), ".unity");
                        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                        EditorSceneManager.SaveScene(scene, path);
                        return Ok(id, SerializeScene(scene));
                    }
                    case "scene.open":
                    {
                        var path = ExtractString(line, "path");
                        var additive = line.IndexOf("\"additive\":true", StringComparison.OrdinalIgnoreCase) >= 0;
                        var mode = additive ? OpenSceneMode.Additive : OpenSceneMode.Single;
                        var scene = EditorSceneManager.OpenScene(path, mode);
                        return Ok(id, SerializeScene(scene));
                    }
                    case "scene.save":
                        EditorSceneManager.SaveOpenScenes();
                        return Ok(id, "null");
                    case "scene.unload":
                    {
                        var path = ExtractString(line, "path");
                        for (var i = 0; i < SceneManager.sceneCount; i++)
                        {
                            var s = SceneManager.GetSceneAt(i);
                            if (s.path == path)
                            {
                                EditorSceneManager.CloseScene(s, true);
                                return Ok(id, "true");
                            }
                        }
                        return Ok(id, "false");
                    }
                    case "scene.setActive":
                    {
                        var path = ExtractString(line, "path");
                        for (var i = 0; i < SceneManager.sceneCount; i++)
                        {
                            var s = SceneManager.GetSceneAt(i);
                            if (s.path == path)
                            {
                                SceneManager.SetActiveScene(s);
                                return Ok(id, "true");
                            }
                        }
                        return Ok(id, "false");
                    }
                    case "go.create":
                    {
                        var name = ExtractString(line, "name") ?? "GameObject";
                        var primitive = ExtractString(line, "primitive");
                        GameObject go;
                        if (!string.IsNullOrEmpty(primitive) && Enum.TryParse(primitive, true, out PrimitiveType pt))
                            go = GameObject.CreatePrimitive(pt);
                        else
                            go = new GameObject(name);
                        go.name = name;
                        var parent = ExtractString(line, "parent");
                        if (!string.IsNullOrEmpty(parent))
                        {
                            var p = FindGo(parent);
                            if (p != null) go.transform.SetParent(p.transform, false);
                        }
                        return Ok(id, SerializeGo(go));
                    }
                    case "go.find":
                    {
                        var idOrPath = ExtractString(line, "idOrPath");
                        var go = FindGo(idOrPath);
                        return Ok(id, go == null ? "null" : SerializeGo(go));
                    }
                    case "go.findMany":
                    {
                        var name = ExtractString(line, "name");
                        var tag = ExtractString(line, "tag");
                        var componentType = ExtractString(line, "componentType");
                        var list = new List<string>();
                        foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                        {
                            if (!string.IsNullOrEmpty(name) &&
                                !go.name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                                go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                            if (!string.IsNullOrEmpty(tag))
                            {
                                try
                                {
                                    if (!go.CompareTag(tag) && !string.Equals(go.tag, tag, StringComparison.OrdinalIgnoreCase))
                                        continue;
                                }
                                catch { continue; }
                            }
                            if (!string.IsNullOrEmpty(componentType) && go.GetComponent(componentType) == null)
                                continue;
                            list.Add(SerializeGo(go));
                        }
                        return Ok(id, "[" + string.Join(",", list) + "]");
                    }
                    case "go.all":
                    {
                        var list = new List<string>();
                        foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                            list.Add(SerializeGo(go));
                        return Ok(id, "[" + string.Join(",", list) + "]");
                    }
                    case "go.delete":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        if (go == null) return Ok(id, "false");
                        UnityEngine.Object.DestroyImmediate(go);
                        return Ok(id, "true");
                    }
                    case "go.setTransform":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        if (go == null) return Ok(id, "false");
                        // Partial updates via nested numbers if present
                        TryApplyVec(line, "position", (x, y, z) => go.transform.position = new Vector3(x, y, z), go.transform.position);
                        TryApplyVec(line, "rotation", (x, y, z) => go.transform.eulerAngles = new Vector3(x, y, z), go.transform.eulerAngles);
                        TryApplyVec(line, "scale", (x, y, z) => go.transform.localScale = new Vector3(x, y, z), go.transform.localScale);
                        return Ok(id, "true");
                    }
                    case "go.setParent":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        if (go == null) return Ok(id, "false");
                        var parent = ExtractString(line, "parent");
                        go.transform.SetParent(string.IsNullOrEmpty(parent) ? null : FindGo(parent)?.transform, true);
                        return Ok(id, "true");
                    }
                    case "go.setActive":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        if (go == null) return Ok(id, "false");
                        var active = line.IndexOf("\"active\":false", StringComparison.OrdinalIgnoreCase) < 0;
                        go.SetActive(active);
                        return Ok(id, "true");
                    }
                    case "go.rename":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        if (go == null) return Ok(id, "false");
                        go.name = ExtractString(line, "newName") ?? go.name;
                        return Ok(id, "true");
                    }
                    case "go.setTag":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        if (go == null) return Ok(id, "false");
                        go.tag = ExtractString(line, "tag") ?? "Untagged";
                        return Ok(id, "true");
                    }
                    case "go.setLayer":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        if (go == null) return Ok(id, "false");
                        go.layer = ExtractInt(line, "layer") ?? 0;
                        return Ok(id, "true");
                    }
                    case "go.duplicate":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        if (go == null) return Ok(id, "null");
                        var copy = UnityEngine.Object.Instantiate(go);
                        var newName = ExtractString(line, "newName");
                        if (!string.IsNullOrEmpty(newName)) copy.name = newName;
                        return Ok(id, SerializeGo(copy));
                    }
                    case "comp.add":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        var typeName = ExtractString(line, "typeName");
                        if (go == null || string.IsNullOrEmpty(typeName)) return Ok(id, "false");
                        var t = FindType(typeName);
                        if (t == null) return Ok(id, "false");
                        if (go.GetComponent(t) == null) go.AddComponent(t);
                        return Ok(id, "true");
                    }
                    case "comp.remove":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        var typeName = ExtractString(line, "typeName");
                        if (go == null || string.IsNullOrEmpty(typeName)) return Ok(id, "false");
                        var c = go.GetComponent(typeName);
                        if (c == null) return Ok(id, "false");
                        UnityEngine.Object.DestroyImmediate(c);
                        return Ok(id, "true");
                    }
                    case "comp.get":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        var typeName = ExtractString(line, "typeName");
                        if (go == null) return Ok(id, "null");
                        var c = go.GetComponent(typeName);
                        if (c == null) return Ok(id, "null");
                        return Ok(id, "{\"typeName\":" + JsonString(typeName) + ",\"properties\":{}}");
                    }
                    case "comp.modify":
                    {
                        var go = FindGo(ExtractString(line, "idOrPath"));
                        var typeName = ExtractString(line, "typeName");
                        if (go == null || string.IsNullOrEmpty(typeName)) return Ok(id, "false");
                        var c = go.GetComponent(typeName);
                        if (c == null) return Ok(id, "false");
                        return Ok(id, ApplyComponentProperties(c, line) ? "true" : "false");
                    }
                    case "comp.listTypes":
                        return Ok(id, "[\"Transform\",\"Rigidbody\",\"BoxCollider\",\"MeshRenderer\",\"Camera\",\"Light\"]");
                    case "assets.materialCreate":
                    {
                        var path = NormalizeAsset(ExtractString(line, "path"), ".mat");
                        var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color"));
                        AssetDatabase.CreateAsset(mat, path);
                        return Ok(id, "{\"path\":" + JsonString(path) + ",\"name\":" + JsonString(Path.GetFileNameWithoutExtension(path)) + ",\"color\":\"#FFFFFF\",\"shader\":\"Standard\"}");
                    }
                    case "assets.materialAssign":
                    {
                        var go = FindGo(ExtractString(line, "target"));
                        var path = ExtractString(line, "path");
                        if (go == null || string.IsNullOrEmpty(path)) return Ok(id, "false");
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                        var r = go.GetComponent<Renderer>();
                        if (r == null || mat == null) return Ok(id, "false");
                        r.sharedMaterial = mat;
                        return Ok(id, "true");
                    }
                    case "assets.prefabCreate":
                    {
                        var path = NormalizeAsset(ExtractString(line, "path"), ".prefab");
                        var go = FindGo(ExtractString(line, "source"));
                        if (go == null) return Fail(id, "source GO not found");
                        PrefabUtility.SaveAsPrefabAsset(go, path);
                        return Ok(id, "{\"path\":" + JsonString(path) + ",\"name\":" + JsonString(Path.GetFileNameWithoutExtension(path)) + ",\"sourceObjectId\":" + JsonString(go.GetInstanceID().ToString()) + "}");
                    }
                    case "assets.prefabInstantiate":
                    {
                        var path = ExtractString(line, "path");
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab == null) return Ok(id, "null");
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        return Ok(id, SerializeGo(inst));
                    }
                    case "assets.find":
                        return Ok(id, FindAssetsJson(ExtractString(line, "filter"), ExtractString(line, "kind")));
                    case "assets.folderCreate":
                    {
                        var path = ExtractString(line, "path") ?? "Assets";
                        if (!AssetDatabase.IsValidFolder(path))
                        {
                            var parts = path.Split('/');
                            var acc = parts[0];
                            for (var i = 1; i < parts.Length; i++)
                            {
                                var next = acc + "/" + parts[i];
                                if (!AssetDatabase.IsValidFolder(next))
                                    AssetDatabase.CreateFolder(acc, parts[i]);
                                acc = next;
                            }
                        }
                        return Ok(id, "true");
                    }
                    case "assets.delete":
                        return Ok(id, AssetDatabase.DeleteAsset(ExtractString(line, "path") ?? "") ? "true" : "false");
                    case "assets.copy":
                        return Ok(id, AssetDatabase.CopyAsset(ExtractString(line, "fromPath"), ExtractString(line, "toPath")) ? "true" : "false");
                    case "assets.move":
                    {
                        var err = AssetDatabase.MoveAsset(ExtractString(line, "fromPath"), ExtractString(line, "toPath"));
                        return Ok(id, string.IsNullOrEmpty(err) ? "true" : "false");
                    }
                    case "assets.refresh":
                        AssetDatabase.Refresh();
                        return Ok(id, "null");
                    case "assets.listShaders":
                        return Ok(id, "[\"Standard\",\"Unlit/Color\",\"Sprites/Default\",\"Universal Render Pipeline/Lit\",\"UI/Default\"]");
                    case "selection.get":
                        return Ok(id, SerializeSelection());
                    case "selection.set":
                    {
                        ApplySelection(line);
                        return Ok(id, "null");
                    }
                    case "package.list":
                        return Ok(id, ListPackagesJson());
                    case "package.add":
                    {
                        var pkg = ExtractString(line, "package") ?? "com.unity.modules.ui";
                        return Ok(id, AddPackageJson(pkg));
                    }
                    case "package.remove":
                    {
                        var pkg = ExtractString(line, "package") ?? "";
                        return Ok(id, RemovePackageJson(pkg));
                    }
                    case "package.search":
                    {
                        var q = ExtractString(line, "query") ?? "";
                        return Ok(id, SearchPackagesJson(q));
                    }
                    case "menu.list":
                    {
                        var filter = ExtractString(line, "filter");
                        return Ok(id, ListMenusJson(filter));
                    }
                    case "menu.execute":
                    {
                        var path = ExtractString(line, "path");
                        return Ok(id, EditorApplication.ExecuteMenuItem(path) ? "true" : "false");
                    }
                    case "screenshot.capture":
                        return Ok(id, CaptureScreenshotJson(
                            ExtractString(line, "source") ?? "game_view",
                            ExtractString(line, "targetId"),
                            ExtractInt(line, "width") ?? 1280,
                            ExtractInt(line, "height") ?? 720));
                    case "profiler.get":
                        return Ok(id, ProfilerGetJson());
                    case "profiler.setEnabled":
                    {
                        var enabled = line.IndexOf("\"enabled\":true", StringComparison.OrdinalIgnoreCase) >= 0
                                      || line.IndexOf("\"enabled\": true", StringComparison.OrdinalIgnoreCase) >= 0;
                        _profilerEnabled = enabled;
                        try { Profiler.enabled = enabled; } catch { /* optional */ }
                        return Ok(id, "null");
                    }
                    case "profiler.clear":
                        // Editor profiler clear is best-effort; keep snapshot counters honest.
                        return Ok(id, "null");
                    case "profiler.save":
                    {
                        var path = ExtractString(line, "path") ?? "Temp/unity-comdr-profiler.json";
                        ProfilerSaves[path] = ProfilerGetJson();
                        return Ok(id, "null");
                    }
                    case "profiler.load":
                    {
                        var path = ExtractString(line, "path") ?? "";
                        if (ProfilerSaves.TryGetValue(path, out var snap))
                            return Ok(id, snap);
                        return Ok(id, "null");
                    }
                    default:
                        return Fail(id, "Unknown method: " + method);
                }
            }
            catch (Exception ex)
            {
                return Fail(id, ex.Message);
            }
        }

        // --- helpers ---

        private static GameObject FindGo(string idOrPath)
        {
            if (string.IsNullOrEmpty(idOrPath)) return null;
            if (int.TryParse(idOrPath, out var instanceId))
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                if (obj != null) return obj;
            }
            var byName = GameObject.Find(idOrPath);
            if (byName != null) return byName;
            // path walk
            var parts = idOrPath.Split('/');
            GameObject current = null;
            foreach (var part in parts)
            {
                if (current == null)
                    current = GameObject.Find(part);
                else
                {
                    Transform child = null;
                    for (var i = 0; i < current.transform.childCount; i++)
                    {
                        var c = current.transform.GetChild(i);
                        if (c.name == part) { child = c; break; }
                    }
                    current = child != null ? child.gameObject : null;
                }
                if (current == null) return null;
            }
            return current;
        }

        private static Type FindType(string typeName)
        {
            var t = Type.GetType("UnityEngine." + typeName + ", UnityEngine")
                    ?? Type.GetType(typeName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(typeName) ?? asm.GetType("UnityEngine." + typeName);
                if (t != null) return t;
            }
            return null;
        }

        private static string SerializeGo(GameObject go)
        {
            var comps = new List<string>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                comps.Add(JsonString(c.GetType().Name));
            }
            var childIds = new List<string>();
            for (var i = 0; i < go.transform.childCount; i++)
                childIds.Add(JsonString(go.transform.GetChild(i).gameObject.GetInstanceID().ToString()));
            var p = go.transform.position;
            var r = go.transform.eulerAngles;
            var s = go.transform.localScale;
            return "{"
                + "\"id\":" + JsonString(go.GetInstanceID().ToString()) + ","
                + "\"name\":" + JsonString(go.name) + ","
                + "\"parentId\":" + (go.transform.parent ? JsonString(go.transform.parent.gameObject.GetInstanceID().ToString()) : "null") + ","
                + "\"active\":" + (go.activeSelf ? "true" : "false") + ","
                + "\"tag\":" + JsonString(SafeTag(go)) + ","
                + "\"layer\":" + go.layer + ","
                + "\"transform\":{\"position\":{\"x\":" + F(p.x) + ",\"y\":" + F(p.y) + ",\"z\":" + F(p.z)
                + "},\"rotationEuler\":{\"x\":" + F(r.x) + ",\"y\":" + F(r.y) + ",\"z\":" + F(r.z)
                + "},\"scale\":{\"x\":" + F(s.x) + ",\"y\":" + F(s.y) + ",\"z\":" + F(s.z) + "}},"
                + "\"components\":[" + string.Join(",", comps.ConvertAll(c => "{\"typeName\":" + c + ",\"properties\":{}}")) + "],"
                + "\"childIds\":[" + string.Join(",", childIds) + "]"
                + "}";
        }

        private static string SerializeScene(Scene scene)
        {
            var roots = new List<string>();
            if (scene.IsValid() && scene.isLoaded)
            {
                foreach (var go in scene.GetRootGameObjects())
                    roots.Add(JsonString(go.GetInstanceID().ToString()));
            }
            return "{"
                + "\"path\":" + JsonString(scene.path ?? "") + ","
                + "\"name\":" + JsonString(scene.name ?? "") + ","
                + "\"dirty\":" + (scene.isDirty ? "true" : "false") + ","
                + "\"isLoaded\":" + (scene.isLoaded ? "true" : "false") + ","
                + "\"rootObjectIds\":[" + string.Join(",", roots) + "]"
                + "}";
        }

        private static string SafeTag(GameObject go)
        {
            try { return go.tag ?? "Untagged"; }
            catch { return "Untagged"; }
        }

        private static string F(float v) =>
            v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string FindAssetsJson(string filter, string kind)
        {
            var search = string.IsNullOrEmpty(filter) ? "" : filter.Replace("*", "");
            if (!string.IsNullOrEmpty(kind))
            {
                if (kind.Equals("Script", StringComparison.OrdinalIgnoreCase)) search = "t:Script " + search;
                else if (kind.Equals("Material", StringComparison.OrdinalIgnoreCase)) search = "t:Material " + search;
                else if (kind.Equals("Prefab", StringComparison.OrdinalIgnoreCase)) search = "t:Prefab " + search;
                else if (kind.Equals("Folder", StringComparison.OrdinalIgnoreCase)) search = "t:Folder " + search;
            }
            var guids = AssetDatabase.FindAssets(search.Trim());
            var parts = new List<string>();
            var max = Math.Min(guids.Length, 100);
            for (var i = 0; i < max; i++)
            {
                var p = AssetDatabase.GUIDToAssetPath(guids[i]);
                var k = "Other";
                if (p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) k = "Script";
                else if (p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) k = "Material";
                else if (p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) k = "Prefab";
                else if (AssetDatabase.IsValidFolder(p)) k = "Folder";
                parts.Add("{\"path\":" + JsonString(p) + ",\"kind\":" + JsonString(k) + "}");
            }
            return "[" + string.Join(",", parts) + "]";
        }

        private static string SerializeSelection()
        {
            var goIds = new List<string>();
            foreach (var go in Selection.gameObjects)
                goIds.Add(JsonString(go.GetInstanceID().ToString()));
            var assets = new List<string>();
            foreach (var guid in Selection.assetGUIDs)
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(p)) assets.Add(JsonString(p));
            }
            return "{\"gameObjectIds\":[" + string.Join(",", goIds) + "],\"assetPaths\":[" + string.Join(",", assets) + "]}";
        }

        private static void ApplySelection(string line)
        {
            // BridgeClient sends: args.gameObjectIds:[...] and args.assetPaths:[...]
            // Domain skill may also send goIds CSV.
            var list = new List<UnityEngine.Object>();
            foreach (var idOrPath in ExtractStringArray(line, "gameObjectIds"))
            {
                var go = FindGo(idOrPath);
                if (go != null) list.Add(go);
            }
            var goCsv = ExtractString(line, "goIds");
            if (!string.IsNullOrEmpty(goCsv))
            {
                foreach (var part in goCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var go = FindGo(part.Trim());
                    if (go != null) list.Add(go);
                }
            }
            foreach (var assetPath in ExtractStringArray(line, "assetPaths"))
            {
                var obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (obj != null) list.Add(obj);
            }
            Selection.objects = list.ToArray();
        }

        /// <summary>
        /// Parse JSON string array values for a key (supports BridgeClient wire format).
        /// </summary>
        private static List<string> ExtractStringArray(string json, string key)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return result;
            var marker = "\"" + key + "\":";
            var idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return result;
            var start = idx + marker.Length;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length) return result;
            if (json[start] == 'n') return result;
            if (json[start] == '"')
            {
                var single = ExtractString(json, key);
                if (!string.IsNullOrEmpty(single)) result.Add(single);
                return result;
            }
            if (json[start] != '[') return result;
            start++;
            while (start < json.Length)
            {
                while (start < json.Length && (char.IsWhiteSpace(json[start]) || json[start] == ',')) start++;
                if (start >= json.Length || json[start] == ']') break;
                if (json[start] != '"') break;
                start++;
                var sb = new StringBuilder();
                for (; start < json.Length; start++)
                {
                    var c = json[start];
                    if (c == '\\' && start + 1 < json.Length)
                    {
                        var n = json[start + 1];
                        if (n == 'n') sb.Append('\n');
                        else if (n == 'r') sb.Append('\r');
                        else if (n == 't') sb.Append('\t');
                        else if (n == '"') sb.Append('"');
                        else if (n == '\\') sb.Append('\\');
                        else sb.Append(n);
                        start++;
                        continue;
                    }
                    if (c == '"') { start++; break; }
                    sb.Append(c);
                }
                result.Add(sb.ToString());
            }
            return result;
        }

        private static bool ApplyComponentProperties(Component c, string line)
        {
            // "properties":{"mass":5,"useGravity":false} from BridgeClient args
            var marker = "\"properties\":";
            var idx = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            var start = line.IndexOf('{', idx + marker.Length);
            if (start < 0) return false;
            var depth = 0;
            var end = -1;
            for (var i = start; i < line.Length; i++)
            {
                if (line[i] == '{') depth++;
                else if (line[i] == '}')
                {
                    depth--;
                    if (depth == 0) { end = i; break; }
                }
            }
            if (end < 0) return false;
            var propsJson = line.Substring(start, end - start + 1);
            var so = new SerializedObject(c);
            var applied = 0;
            // naive key scan: "key":
            var pos = 1;
            while (pos < propsJson.Length - 1)
            {
                var q = propsJson.IndexOf('"', pos);
                if (q < 0) break;
                var q2 = propsJson.IndexOf('"', q + 1);
                if (q2 < 0) break;
                var propName = propsJson.Substring(q + 1, q2 - q - 1);
                if (propName == "properties") { pos = q2 + 1; continue; }
                var colon = propsJson.IndexOf(':', q2);
                if (colon < 0) break;
                var valStart = colon + 1;
                while (valStart < propsJson.Length && char.IsWhiteSpace(propsJson[valStart])) valStart++;
                if (valStart >= propsJson.Length) break;
                string valToken;
                var valEnd = valStart;
                if (propsJson[valStart] == '"')
                {
                    valEnd = propsJson.IndexOf('"', valStart + 1);
                    if (valEnd < 0) break;
                    valToken = propsJson.Substring(valStart + 1, valEnd - valStart - 1);
                    valEnd++;
                }
                else if (propsJson[valStart] == '{' || propsJson[valStart] == '[')
                {
                    pos = q2 + 1;
                    continue; // skip nested objects
                }
                else
                {
                    while (valEnd < propsJson.Length && propsJson[valEnd] != ',' && propsJson[valEnd] != '}')
                        valEnd++;
                    valToken = propsJson.Substring(valStart, valEnd - valStart).Trim();
                }
                var sp = so.FindProperty(propName);
                if (sp != null)
                {
                    if (sp.propertyType == SerializedPropertyType.Float && float.TryParse(valToken, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                    { sp.floatValue = f; applied++; }
                    else if (sp.propertyType == SerializedPropertyType.Integer && int.TryParse(valToken, out var i))
                    { sp.intValue = i; applied++; }
                    else if (sp.propertyType == SerializedPropertyType.Boolean)
                    { sp.boolValue = valToken.Equals("true", StringComparison.OrdinalIgnoreCase); applied++; }
                    else if (sp.propertyType == SerializedPropertyType.String)
                    { sp.stringValue = valToken; applied++; }
                }
                pos = Math.Max(valEnd, q2 + 1);
            }
            if (applied > 0)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(c);
                return true;
            }
            return false;
        }

        private static string ListPackagesJson()
        {
            // Filesystem snapshot (manifest + PackageCache) — avoids main-thread UPM hang.
            return ListPackagesFromProjectFiles();
        }

        private static string ListPackagesFromProjectFiles()
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
                var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
                var parts = new List<string>();
                if (File.Exists(manifestPath))
                {
                    var text = File.ReadAllText(manifestPath);
                    // "com.unity.x": "1.2.3"
                    var rx = new System.Text.RegularExpressions.Regex("\"(com\\.[^\"]+)\"\\s*:\\s*\"([^\"]+)\"");
                    foreach (System.Text.RegularExpressions.Match m in rx.Matches(text))
                    {
                        var name = m.Groups[1].Value;
                        var ver = m.Groups[2].Value;
                        if (name == "dependencies") continue;
                        parts.Add("{\"name\":" + JsonString(name) + ",\"version\":" + JsonString(ver) +
                                  ",\"source\":\"manifest\",\"displayName\":" + JsonString(name) + "}");
                    }
                }
                // Also scan PackageCache folder names for installed packages
                var cache = Path.Combine(projectRoot, "Library", "PackageCache");
                if (Directory.Exists(cache))
                {
                    foreach (var dir in Directory.GetDirectories(cache))
                    {
                        var folder = Path.GetFileName(dir);
                        // com.unity.x@1.2.3
                        var at = folder.LastIndexOf('@');
                        var name = at > 0 ? folder.Substring(0, at) : folder;
                        var ver = at > 0 ? folder.Substring(at + 1) : "";
                        if (parts.Exists(p => p.Contains("\"name\":" + JsonString(name)))) continue;
                        parts.Add("{\"name\":" + JsonString(name) + ",\"version\":" + JsonString(ver) +
                                  ",\"source\":\"packageCache\",\"displayName\":" + JsonString(name) + "}");
                    }
                }
                return "[" + string.Join(",", parts) + "]";
            }
            catch (Exception ex)
            {
                return "[{\"name\":\"error\",\"version\":\"0\",\"source\":\"local\",\"displayName\":" + JsonString(ex.Message) + "}]";
            }
        }

        private static string AddPackageJson(string packageId)
        {
            // Write dependency into Packages/manifest.json (production-safe without blocking UPM main-thread wait).
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
                var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
                if (!File.Exists(manifestPath))
                    return "{\"name\":" + JsonString(packageId) + ",\"version\":\"\",\"source\":\"manifest\",\"displayName\":\"manifest missing\"}";
                var name = packageId;
                var ver = "1.0.0";
                if (packageId.Contains("@"))
                {
                    var bits = packageId.Split('@');
                    name = bits[0];
                    ver = bits.Length > 1 ? bits[1] : ver;
                }
                var text = File.ReadAllText(manifestPath);
                if (text.Contains("\"" + name + "\""))
                {
                    // update version loosely
                    text = System.Text.RegularExpressions.Regex.Replace(text,
                        "\"" + System.Text.RegularExpressions.Regex.Escape(name) + "\"\\s*:\\s*\"[^\"]*\"",
                        "\"" + name + "\": \"" + ver + "\"");
                }
                else
                {
                    var insert = "    \"" + name + "\": \"" + ver + "\",\n";
                    var depIdx = text.IndexOf("\"dependencies\"", StringComparison.OrdinalIgnoreCase);
                    if (depIdx >= 0)
                    {
                        var brace = text.IndexOf('{', depIdx);
                        if (brace >= 0)
                            text = text.Insert(brace + 1, "\n" + insert);
                    }
                }
                File.WriteAllText(manifestPath, text);
                AssetDatabase.Refresh();
                return "{\"name\":" + JsonString(name) + ",\"version\":" + JsonString(ver) +
                       ",\"source\":\"manifest\",\"displayName\":" + JsonString(name) + "}";
            }
            catch (Exception ex)
            {
                return "{\"name\":" + JsonString(packageId) + ",\"version\":\"\",\"source\":\"manifest\",\"displayName\":" + JsonString(ex.Message) + "}";
            }
        }

        private static string RemovePackageJson(string packageName)
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
                var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
                if (!File.Exists(manifestPath)) return "false";
                var text = File.ReadAllText(manifestPath);
                var next = System.Text.RegularExpressions.Regex.Replace(text,
                    "\\s*\"" + System.Text.RegularExpressions.Regex.Escape(packageName) + "\"\\s*:\\s*\"[^\"]*\"\\s*,?",
                    "");
                if (next == text) return "false";
                File.WriteAllText(manifestPath, next);
                AssetDatabase.Refresh();
                return "true";
            }
            catch { return "false"; }
        }

        private static string SearchPackagesJson(string query)
        {
            // Search installed/manifest + package cache (no blocking UPM Search).
            query = query ?? "";
            var listJson = ListPackagesFromProjectFiles();
            if (string.IsNullOrEmpty(query)) return listJson;
            try
            {
                // Filter lines containing query
                var parts = new List<string>();
                var rx = new System.Text.RegularExpressions.Regex("\\{[^}]+\\}");
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(listJson))
                {
                    if (m.Value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        parts.Add(m.Value);
                }
                // Always include catalog-like hints for common packages
                var hints = new[] { "com.unity.cinemachine", "com.unity.addressables", "com.unity.timeline", "com.unity.probuilder" };
                foreach (var h in hints)
                {
                    if (h.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        !parts.Exists(p => p.Contains(h)))
                        parts.Add("{\"name\":" + JsonString(h) + ",\"version\":\"\",\"source\":\"registry\",\"displayName\":" + JsonString(h) + "}");
                }
                return "[" + string.Join(",", parts) + "]";
            }
            catch { return "[]"; }
        }

        private static string ListMenusJson(string filter)
        {
            var parts = new List<string>();
            foreach (var path in BuiltinMenuCatalog)
            {
                if (!string.IsNullOrEmpty(filter) && path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var cat = path.Contains("/") ? path.Substring(0, path.IndexOf('/')) : path;
                parts.Add("{\"path\":" + JsonString(path) + ",\"category\":" + JsonString(cat) + "}");
            }
            return "[" + string.Join(",", parts) + "]";
        }

        private static string ProfilerGetJson()
        {
            long mono = 0;
            long total = 0;
            try { mono = Profiler.GetMonoUsedSizeLong(); } catch { mono = 0; }
            try { total = Profiler.GetTotalAllocatedMemoryLong(); } catch { total = 0; }
            var dt = Time.deltaTime > 0 ? Time.deltaTime * 1000f : 16.67f;
            var fps = dt > 0 ? 1000f / dt : 60f;
            return "{"
                + "\"enabled\":" + (_profilerEnabled ? "true" : "false") + ","
                + "\"deltaTimeMs\":" + F(dt) + ","
                + "\"fps\":" + F(fps) + ","
                + "\"monoUsedBytes\":" + mono + ","
                + "\"totalAllocatedBytes\":" + total + ","
                + "\"enabledModules\":[\"CPU\",\"Memory\",\"Rendering\"]"
                + "}";
        }

        private static string CaptureScreenshotJson(string source, string targetId, int width, int height)
        {
            width = Math.Max(16, Math.Min(width, 4096));
            height = Math.Max(16, Math.Min(height, 4096));
            string marker;
            string note;
            string format = "png-base64";
            try
            {
                Camera cam = null;
                if (!string.IsNullOrEmpty(targetId))
                {
                    var go = FindGo(targetId);
                    if (go != null) cam = go.GetComponent<Camera>();
                }
                if (cam == null) cam = Camera.main;
                if (cam == null) cam = UnityEngine.Object.FindObjectOfType<Camera>();

                if (cam != null && (source == "camera" || source == "game_view" || source == "isolated"))
                {
                    var rt = new RenderTexture(width, height, 24);
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    cam.targetTexture = prev;
                    RenderTexture.active = null;
                    var bytes = tex.EncodeToPNG();
                    UnityEngine.Object.DestroyImmediate(tex);
                    rt.Release();
                    var b64 = Convert.ToBase64String(bytes);
                    // Also write full PNG to disk so large payloads are recoverable without truncating base64.
                    var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
                    var outDir = Path.Combine(projectRoot, "Temp");
                    Directory.CreateDirectory(outDir);
                    var fileName = "unity-comdr-shot-" + Guid.NewGuid().ToString("N") + ".png";
                    var filePath = Path.Combine(outDir, fileName).Replace('\\', '/');
                    File.WriteAllBytes(filePath, bytes);
                    marker = "live-png:" + source + ":" + Guid.NewGuid().ToString("N");
                    note = "Live camera PNG full base64 + filePath length=" + b64.Length;
                    return "{\"source\":" + JsonString(source) + ",\"format\":" + JsonString(format) +
                           ",\"note\":" + JsonString(note) + ",\"payloadMarker\":" + JsonString(marker) +
                           ",\"width\":" + width + ",\"height\":" + height +
                           ",\"filePath\":" + JsonString(filePath) +
                           ",\"pngBase64\":" + JsonString(b64) + "}";
                }

                marker = "live-marker:" + source + ":" + Guid.NewGuid().ToString("N");
                note = "No camera available; returned non-empty live marker (scene_view/game_view without camera).";
                return "{\"source\":" + JsonString(source) + ",\"format\":\"marker\",\"note\":" + JsonString(note) +
                       ",\"payloadMarker\":" + JsonString(marker) + ",\"width\":" + width + ",\"height\":" + height + "}";
            }
            catch (Exception ex)
            {
                marker = "live-error:" + Guid.NewGuid().ToString("N");
                return "{\"source\":" + JsonString(source) + ",\"format\":\"marker\",\"note\":" + JsonString(ex.Message) +
                       ",\"payloadMarker\":" + JsonString(marker) + ",\"width\":" + width + ",\"height\":" + height + "}";
            }
        }

        private static string SerializeState()
        {
            var scene = SceneManager.GetActiveScene();
            return "{"
                + "\"isCompiling\":" + (EditorApplication.isCompiling ? "true" : "false") + ","
                + "\"isPlaying\":" + (EditorApplication.isPlaying ? "true" : "false") + ","
                + "\"isPaused\":" + (EditorApplication.isPaused ? "true" : "false") + ","
                + "\"activeScenePath\":" + JsonString(scene.path ?? "")
                + "}";
        }

        private static string SerializeLogs()
        {
            lock (Gate)
            {
                var parts = new List<string>();
                foreach (var l in Logs)
                {
                    parts.Add("{\"type\":" + JsonString(l.Type) + ",\"message\":" + JsonString(l.Message) + ",\"file\":null,\"line\":0}");
                }
                return "[" + string.Join(",", parts) + "]";
            }
        }

        private static string SerializeStringArray(List<string> items)
        {
            var parts = new List<string>();
            foreach (var s in items) parts.Add(JsonString(s));
            return "[" + string.Join(",", parts) + "]";
        }

        private static void TryApplyVec(string line, string key, Action<float, float, float> apply, Vector3 current)
        {
            // Look for "key":{...}
            var marker = "\"" + key + "\"";
            var idx = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;
            var brace = line.IndexOf('{', idx);
            if (brace < 0) return;
            var end = line.IndexOf('}', brace);
            if (end < 0) return;
            var slice = line.Substring(brace, end - brace + 1);
            var x = ExtractFloatFromObject(slice, "x") ?? current.x;
            var y = ExtractFloatFromObject(slice, "y") ?? current.y;
            var z = ExtractFloatFromObject(slice, "z") ?? current.z;
            apply(x, y, z);
        }

        private static float? ExtractFloatFromObject(string obj, string key)
        {
            var marker = "\"" + key + "\":";
            var idx = obj.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var start = idx + marker.Length;
            while (start < obj.Length && char.IsWhiteSpace(obj[start])) start++;
            var end = start;
            while (end < obj.Length && (char.IsDigit(obj[end]) || obj[end] == '-' || obj[end] == '+' || obj[end] == '.' || obj[end] == 'e' || obj[end] == 'E'))
                end++;
            if (float.TryParse(obj.Substring(start, end - start), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                return f;
            return null;
        }

        private static int? ExtractInt(string json, string key)
        {
            var s = ExtractString(json, key);
            if (int.TryParse(s, out var i)) return i;
            var marker = "\"" + key + "\":";
            var idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var start = idx + marker.Length;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            var end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
            if (int.TryParse(json.Substring(start, end - start), out i)) return i;
            return null;
        }

        private static string ExtractString(string json, string key)
        {
            var marker = "\"" + key + "\":";
            var idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var start = idx + marker.Length;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length) return null;
            if (json[start] == 'n') return null; // null
            if (json[start] != '"') return null;
            start++;
            var sb = new StringBuilder();
            for (var i = start; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    var n = json[i + 1];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'u':
                            // \uXXXX — same as BridgeJson.ExtractString: advance five chars (u+4 hex) then continue.
                            if (i + 5 < json.Length &&
                                int.TryParse(json.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out var code))
                            {
                                sb.Append((char)code);
                                i += 5;
                                continue;
                            }
                            sb.Append(n);
                            break;
                        default:
                            sb.Append(n);
                            break;
                    }
                    i++;
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string ExtractNestedString(string json, string key) => ExtractString(json, key);

        private static string NormalizeAsset(string path, string ext)
        {
            path = (path ?? "Assets/Untitled").Replace('\\', '/');
            if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) path += ext;
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                path = "Assets/" + path.TrimStart('/');
            return path;
        }

        private static string ToFull(string assetPath)
        {
            var data = Application.dataPath.Replace('\\', '/');
            var root = data.Substring(0, data.Length - "Assets".Length);
            return Path.GetFullPath(Path.Combine(root, assetPath));
        }

        private static string JsonString(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
        }

        private static string Ok(string id, string resultJson) =>
            "{\"id\":" + JsonString(id) + ",\"ok\":true,\"result\":" + resultJson + "}";

        private static string Fail(string id, string error) =>
            "{\"id\":" + JsonString(id ?? "") + ",\"ok\":false,\"error\":" + JsonString(error) + "}";

        private sealed class LogEntry
        {
            public string Type;
            public string Message;
            public string StackTrace;
            public string File;
            public int Line;
        }
    }
}
#endif
