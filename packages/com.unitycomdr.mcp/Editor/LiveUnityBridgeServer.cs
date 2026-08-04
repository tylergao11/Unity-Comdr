#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;


namespace UnityComdr.UnityEditor
{
    /// <summary>
    /// Live Editor TCP bridge. MCP host (BridgeClientEditorHost) connects here so the same
    /// tool handlers drive real Unity Editor state when the Editor is open.
    /// Protocol: one JSON request/response line per message (see BridgeProtocol in Core).
    /// <para>
    /// Main-thread dispatch (borrow-plan R stage): pattern port of
    /// CoplayDev/unity-mcp <c>TransportCommandDispatcher</c>
    /// (queue + permanent <c>EditorApplication.update</c> drain +
    /// <c>SynchronizationContext.Post</c> / <c>QueuePlayerLoopUpdate</c> wake)
    /// and CoderGamester/mcp-unity <c>Editor/UnityBridge</c> update-drained queue
    /// (requests continue while Editor is unfocused). See THIRD_PARTY.md.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class LiveUnityBridgeServer
    {
        public const int DefaultPort = 17890;
        public static bool IsRunning { get; private set; }
        public static string Status { get; private set; } = "Stopped";
        /// <summary>Port the listener bound to (FR-I3 doctor).</summary>
        public static int ListeningPort { get; private set; } = DefaultPort;
        /// <summary>UTC time of last inbound client request line (FR-I3 doctor).</summary>
        public static DateTime? LastClientCallUtc { get; private set; }
        /// <summary>Method of last inbound client request (FR-I3 doctor).</summary>
        public static string LastMethod { get; private set; }

        private static TcpListener _listener;
        private static CancellationTokenSource _cts;
        private static readonly object Gate = new object();
        private static readonly List<LogEntry> Logs = new List<LogEntry>();
        private static Thread _acceptThread;
        private static bool _profilerEnabled;
        private static readonly Dictionary<string, string> ProfilerSaves = new Dictionary<string, string>();
        private static string _leaseHolder;
        private static DateTime _leaseExpiresUtc;
        // FR-R1: background-safe busy flags so TCP thread can return immediate busy (PR-5: no silent queue).
        private static volatile bool _isCompiling;
        private static volatile bool _isReloading;
        private static volatile bool _playTransition;
        // Cached on main thread so TCP-thread doctor probes (ping) never touch SessionState off-main.
        private static volatile int _sessionGenerationCache = 1;
        private static volatile int _compileEpochCache;
        // Coplay TransportCommandDispatcher-style main-thread queue (update-drained).
        private static readonly ConcurrentQueue<Action> MainThreadQueue = new ConcurrentQueue<Action>();
        private static SynchronizationContext _mainThreadContext;
        private static int _mainThreadId;
        private static int _processingFlag;
        // Coplay StdioBridgeHost-style ensure-started while Editor is idle after reload.
        private static double _nextAutoStartAttempt;
        // O1/O2: persist across domain reload via SessionState (statics reset on reload).
        private const string SessionGenerationKey = "UnityComdr.SessionGeneration";
        private const string CompileEpochKey = "UnityComdr.CompileEpoch";
        /// <summary>Curated whitelist only — not a full Unity menu tree (Claim LIMITED).</summary>
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

        private static readonly Dictionary<string, BridgeTestJob> TestJobs =
            new Dictionary<string, BridgeTestJob>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, BridgePackageJob> PackageJobs =
            new Dictionary<string, BridgePackageJob>(StringComparer.OrdinalIgnoreCase);

        private sealed class BridgeTestJob
        {
            public string JobId;
            public string Status = "running"; // running | completed | failed
            public string Kind = "run"; // run | list
            public string Mode = "EditMode";
            public string Filter;
            public bool? Passed;
            public readonly List<string> ResultLines = new List<string>();
            public string Note;
        }

        /// <summary>Non-blocking UPM op — Request polled on EditorApplication.update (never Thread.Sleep on main).</summary>
        private sealed class BridgePackageJob
        {
            public string JobId;
            public string Status = "running"; // running | completed | failed
            public string Op; // list | add | remove | search
            public Request Request;
            public string ResultJson; // packages array / package object / true|false
            public string Error;
            public string Query;
            public string PackageId;
        }

        static LiveUnityBridgeServer()
        {
            // InitializeOnLoad runs on the Unity main thread — capture context like Coplay.
            _mainThreadContext = SynchronizationContext.Current;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            Application.logMessageReceivedThreaded += OnLog;
            EditorApplication.delayCall += StartIfEnabled;
            // Permanent update hook (Coplay keeps update installed so background commands always process).
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;
        }

        /// <summary>
        /// Main-thread frame tick: refresh lifecycle caches, drain command queue, keep listener up.
        /// Mirrors Coplay <c>TransportCommandDispatcher.ProcessQueue</c> + idle ensure-start.
        /// </summary>
        private static void OnEditorUpdate()
        {
            _isCompiling = EditorApplication.isCompiling;
            _sessionGenerationCache = SessionState.GetInt(SessionGenerationKey, 1);
            _compileEpochCache = SessionState.GetInt(CompileEpochKey, 0);
            ProcessMainThreadQueue();
            PumpPackageJobs();

            if (!IsRunning && !_isReloading && !EditorApplication.isCompiling &&
                EditorApplication.timeSinceStartup >= _nextAutoStartAttempt)
            {
                _nextAutoStartAttempt = EditorApplication.timeSinceStartup + 2.0;
                Start(DefaultPort);
            }
        }

        /// <summary>
        /// Drain queued main-thread work. Coplay uses a re-entrancy flag so nested update is safe.
        /// </summary>
        private static void ProcessMainThreadQueue()
        {
            if (Interlocked.Exchange(ref _processingFlag, 1) == 1)
                return;
            try
            {
                while (MainThreadQueue.TryDequeue(out var work))
                {
                    try { work(); }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _processingFlag, 0);
            }
        }

        /// <summary>
        /// Wake Unity's player/editor loop so queued work runs even when unfocused
        /// (Coplay <c>RequestMainThreadPump</c> + <c>QueuePlayerLoopUpdate</c>).
        /// </summary>
        private static void RequestMainThreadPump()
        {
            void Pump()
            {
                try { EditorApplication.QueuePlayerLoopUpdate(); }
                catch { /* best-effort */ }
                ProcessMainThreadQueue();
            }

            if (_mainThreadContext != null &&
                Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                _mainThreadContext.Post(_ => Pump(), null);
                return;
            }

            Pump();
        }

        /// <summary>
        /// Run <paramref name="work"/> on the Unity main thread and wait (TCP worker path).
        /// Pattern: Coplay <c>RunOnMainThreadAsync</c> / Coder update-drained queue.
        /// </summary>
        private static void RunOnMainThread(Action work, int timeoutMs, out bool timedOut)
        {
            timedOut = false;
            if (work == null) return;

            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId ||
                (_mainThreadId == 0 && Thread.CurrentThread.ManagedThreadId == 1))
            {
                work();
                return;
            }

            var done = new ManualResetEventSlim(false);
            Exception captured = null;
            MainThreadQueue.Enqueue(() =>
            {
                try { work(); }
                catch (Exception ex) { captured = ex; }
                finally { done.Set(); }
            });
            RequestMainThreadPump();
            if (!done.Wait(timeoutMs))
            {
                timedOut = true;
                return;
            }
            if (captured != null)
                throw captured;
        }

        /// <summary>Advance async PackageManager requests without blocking the main thread.</summary>
        private static void PumpPackageJobs()
        {
            if (PackageJobs.Count == 0) return;
            List<string> keys;
            lock (PackageJobs)
            {
                keys = new List<string>(PackageJobs.Keys);
            }
            foreach (var key in keys)
            {
                BridgePackageJob job;
                lock (PackageJobs)
                {
                    if (!PackageJobs.TryGetValue(key, out job)) continue;
                }
                if (job == null || job.Status != "running" || job.Request == null) continue;
                if (!job.Request.IsCompleted) continue;
                try
                {
                    if (job.Request.Status == StatusCode.Failure)
                    {
                        job.Status = "failed";
                        job.Error = job.Request.Error != null ? job.Request.Error.message : "PackageManager request failed";
                    }
                    else
                    {
                        job.ResultJson = SerializePackageRequestResult(job);
                        job.Status = "completed";
                    }
                }
                catch (Exception ex)
                {
                    job.Status = "failed";
                    job.Error = ex.Message;
                }
            }
        }

        private static string SerializePackageRequestResult(BridgePackageJob job)
        {
            switch (job.Op)
            {
                case "list":
                {
                    var listReq = job.Request as ListRequest;
                    var parts = new List<string>();
                    if (listReq != null && listReq.Result != null)
                    {
                        foreach (var p in listReq.Result)
                            parts.Add(SerializePackageInfo(p));
                    }
                    return "[" + string.Join(",", parts) + "]";
                }
                case "add":
                {
                    var addReq = job.Request as AddRequest;
                    if (addReq == null || addReq.Result == null)
                        throw new InvalidOperationException("AddRequest missing result");
                    return SerializePackageInfo(addReq.Result);
                }
                case "remove":
                    return "true";
                case "search":
                {
                    var searchReq = job.Request as SearchRequest;
                    var parts = new List<string>();
                    if (searchReq != null && searchReq.Result != null)
                    {
                        foreach (var p in searchReq.Result)
                            parts.Add(SerializePackageInfo(p));
                    }
                    return "[" + string.Join(",", parts) + "]";
                }
                default:
                    throw new InvalidOperationException("Unknown package op: " + job.Op);
            }
        }

        private static string SerializePackageInfo(UnityEditor.PackageManager.PackageInfo p)
        {
            return "{\"name\":" + JsonString(p.name) +
                   ",\"version\":" + JsonString(p.version) +
                   ",\"source\":" + JsonString(p.source.ToString()) +
                   ",\"displayName\":" + JsonString(string.IsNullOrEmpty(p.displayName) ? p.name : p.displayName) + "}";
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                    _playTransition = true;
                    break;
                case PlayModeStateChange.EnteredEditMode:
                case PlayModeStateChange.EnteredPlayMode:
                    _playTransition = false;
                    break;
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            _isReloading = true;
            // Stop listener so clients observe disconnect as editor_reloading (not a hang).
            Stop();
        }

        private static void OnAfterAssemblyReload()
        {
            // O2: bump sessionGeneration so clients know prior instance ids are invalid.
            var next = SessionState.GetInt(SessionGenerationKey, 1) + 1;
            SessionState.SetInt(SessionGenerationKey, next);
            _sessionGenerationCache = next;
            _compileEpochCache = SessionState.GetInt(CompileEpochKey, 0);
            _isReloading = false;
            _playTransition = false;
            _isCompiling = EditorApplication.isCompiling;
            // beforeAssemblyReload Stop()'d the listener — bring it back without waiting for delayCall.
            EditorApplication.delayCall += StartIfEnabled;
        }

        private static int GetSessionGeneration() =>
            _sessionGenerationCache > 0 ? _sessionGenerationCache : 1;

        private static int GetCompileEpoch() => _compileEpochCache;

        private static void BumpCompileEpoch()
        {
            var next = GetCompileEpoch() + 1;
            SessionState.SetInt(CompileEpochKey, next);
            _compileEpochCache = next;
        }

        private static void OnEditorQuitting()
        {
            _isReloading = true;
            Stop();
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
                    Line = 0,
                    Epoch = GetCompileEpoch()
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
                ListeningPort = port;
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
            // Keep ListeningPort / LastClientCallUtc for doctor after stop.
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

                    // PR-5: immediate busy on TCP thread — never queue silently during transitions.
                    var earlyBusy = TryImmediateBusyResponse(line);
                    if (earlyBusy != null)
                    {
                        try { writer.WriteLine(earlyBusy); }
                        catch { break; }
                        continue;
                    }

                    // Doctor probe: answer from TCP thread using volatile caches (no Unity API).
                    // Host TryConnect(400ms TCP) then ping must not wait on a stalled main thread.
                    var methodEarly = ExtractString(line, "method");
                    if (string.Equals(methodEarly, "ping", StringComparison.OrdinalIgnoreCase))
                    {
                        try { writer.WriteLine(Dispatch(line)); }
                        catch (Exception ex) { try { writer.WriteLine(Fail(null, ex.Message)); } catch { break; } }
                        continue;
                    }

                    // Editor API path: Coplay/Coder main-thread queue (not delayCall-from-worker).
                    string responseJson = null;
                    bool timedOut;
                    try
                    {
                        RunOnMainThread(() =>
                        {
                            try { responseJson = Dispatch(line); }
                            catch (Exception ex) { responseJson = Fail(null, ex.Message); }
                        }, 20000, out timedOut);
                    }
                    catch (Exception ex)
                    {
                        responseJson = Fail(null, ex.Message);
                        timedOut = false;
                    }

                    if (timedOut)
                        responseJson = Fail(null, FormatBusyError("editor_reloading", 5,
                            "Editor main thread timeout (likely reload/compile). Wait and retry."));
                    try { writer.WriteLine(responseJson ?? Fail(null, "empty response")); }
                    catch { break; }
                }
            }
        }

        /// <summary>
        /// Returns a busy Fail response without waiting on the main thread when Editor is transitioning.
        /// Allows ping + editor.getState through so agents can poll lifecycle.
        /// </summary>
        private static void RecordClientCall(string method)
        {
            if (string.IsNullOrEmpty(method)) return;
            LastClientCallUtc = DateTime.UtcNow;
            LastMethod = method;
        }

        private static string TryImmediateBusyResponse(string line)
        {
            var method = ExtractString(line, "method");
            RecordClientCall(method);
            var id = ExtractString(line, "id") ?? Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(method))
                return null;
            if (string.Equals(method, "ping", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(method, "editor.getState", StringComparison.OrdinalIgnoreCase))
                return null;

            string phase;
            int retry;
            string next;
            if (_isReloading)
            {
                phase = "editor_reloading";
                retry = 5;
                next = "Wait for domain reload to finish, reconnect if needed, then retry.";
            }
            else if (_playTransition)
            {
                phase = "play_transition";
                retry = 2;
                next = "Wait for play-mode enter/exit to settle, then retry.";
            }
            else if (_isCompiling)
            {
                phase = "editor_compiling";
                retry = 3;
                next = "Wait for Unity compile to finish, then retry the same tool call.";
            }
            else
                return null;

            return Fail(id, FormatBusyError(phase, retry, next));
        }

        private static string FormatBusyError(string phase, int suggestedRetrySeconds, string nextStep) =>
            phase + " suggestedRetrySeconds=" + suggestedRetrySeconds + " nextStep=" + nextStep;

        private static string CurrentPhase()
        {
            if (_isReloading) return "editor_reloading";
            if (_playTransition) return "play_transition";
            if (_isCompiling || EditorApplication.isCompiling) return "editor_compiling";
            return "connected";
        }

        private static int? SuggestedRetryForPhase(string phase)
        {
            if (phase == "editor_compiling") return 3;
            if (phase == "editor_reloading") return 5;
            if (phase == "play_transition") return 2;
            if (phase == "editor_gone") return 5;
            return null;
        }

        private static string Dispatch(string line)
        {
            // Minimal JSON parse without Core dependency (Unity package is standalone).
            var method = ExtractString(line, "method");
            RecordClientCall(method);
            var id = ExtractString(line, "id") ?? Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(method))
                return Fail(id, "missing method");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            string response = null;
            try
            {
                response = DispatchCore(line, method, id);
                return response;
            }
            finally
            {
                sw.Stop();
                // FR-T3: audit tool methods by default (doctor probes skipped to keep doctor quiet).
                if (!BridgeTrust.IsDoctorMethod(method))
                {
                    var ok = response != null && response.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0;
                    string err = null;
                    if (!ok && response != null)
                        err = ExtractString(response, "error");
                    BridgeTrust.AppendAudit(method, ok, sw.ElapsedMilliseconds, err);
                }
            }
        }

        private static string DispatchCore(string line, string method, string id)
        {
            try
            {
                _isCompiling = EditorApplication.isCompiling;

                // FR-T1: first-connection consent (blocking). Doctor probes remain available.
                string consentError;
                if (!BridgeTrust.EnsureConsent(method, out consentError))
                    return Fail(id, consentError);

                // FR-T2: optional per-method disable via ProjectSettings/UnityComdr.mcp.json
                var trust = BridgeTrust.LoadConfig();
                if (BridgeTrust.IsBridgeMethodDisabled(method, trust))
                    return Fail(id, "tool_disabled: Bridge method '" + method +
                                    "' is disabled in ProjectSettings/UnityComdr.mcp.json.");

                // Main-thread busy gate (race with TCP-thread early check).
                if (!string.Equals(method, "ping", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(method, "editor.getState", StringComparison.OrdinalIgnoreCase))
                {
                    var busy = TryImmediateBusyResponse(line);
                    if (busy != null) return busy;
                }

                switch (method)
                {
                    case "ping":
                    {
                        var phase = CurrentPhase();
                        var retry = SuggestedRetryForPhase(phase);
                        return Ok(id, "{\"pong\":true,\"phase\":" + JsonString(phase) +
                                      (retry.HasValue ? ",\"suggestedRetrySeconds\":" + retry.Value : "") +
                                      ",\"sessionGeneration\":" + GetSessionGeneration() +
                                      ",\"compileEpoch\":" + GetCompileEpoch() + "}");
                    }
                    case "console.get":
                        return Ok(id, SerializeLogs());
                    case "console.clear":
                        lock (Gate) Logs.Clear();
                        // global:: required: we are inside namespace UnityComdr.UnityEditor, so
                        // "UnityEditor.LogEntries" would bind to this namespace, not the Unity API.
                        // LogEntries is internal — resolve by name to avoid CS0122.
                        try
                        {
                            var logEntries = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                            logEntries?.GetMethod("Clear")?.Invoke(null, null);
                        }
                        catch { /* optional */ }
                        return Ok(id, "null");
                    case "console.add":
                    {
                        var msg = ExtractNestedString(line, "message") ?? ExtractString(line, "message") ?? "log";
                        var type = ExtractNestedString(line, "type") ?? "Log";
                        lock (Gate) Logs.Add(new LogEntry { Type = type, Message = msg, Epoch = GetCompileEpoch() });
                        return Ok(id, "null");
                    }
                    case "editor.getState":
                        return Ok(id, SerializeState());
                    case "editor.compile":
                    {
                        // Canonical: CompilationPipeline.RequestScriptCompilation (not Refresh-only).
                        AssetDatabase.Refresh();
                        BumpCompileEpoch();
                        try
                        {
                            CompilationPipeline.RequestScriptCompilation();
                        }
                        catch (Exception ex)
                        {
                            return Fail(id, "RequestScriptCompilation failed: " + ex.Message);
                        }
                        _isCompiling = EditorApplication.isCompiling || true;
                        return Ok(id, "{\"compileEpoch\":" + GetCompileEpoch() +
                                      ",\"sessionGeneration\":" + GetSessionGeneration() +
                                      ",\"hostMode\":\"live\",\"pipeline\":\"CompilationPipeline.RequestScriptCompilation\"}");
                    }
                    case "editor.setPlayMode":
                    {
                        _playTransition = true;
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
                        return Ok(id, SerializeComponentData(c));
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
                        return Ok(id, ListComponentTypesJson(ExtractString(line, "filter")));
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
                        return StartPackageJob(id, "list", null, null);
                    case "package.add":
                        return StartPackageJob(id, "add", ExtractString(line, "package"), null);
                    case "package.remove":
                        return StartPackageJob(id, "remove", ExtractString(line, "package"), null);
                    case "package.search":
                        return StartPackageJob(id, "search", null, ExtractString(line, "query") ?? "");
                    case "package.status":
                        return HandlePackageStatus(id, ExtractString(line, "jobId"));
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
                            ExtractInt(line, "height") ?? 720,
                            ExtractInt(line, "maxResolution") ?? 640,
                            ExtractInt(line, "regionX"),
                            ExtractInt(line, "regionY"),
                            ExtractInt(line, "regionWidth"),
                            ExtractInt(line, "regionHeight"),
                            ExtractString(line, "batch")));
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
                        // Honest: JSON metrics snapshot file (not Unity Profiler binary).
                        var path = ExtractString(line, "path") ?? "Temp/unity-comdr-profiler-metrics.json";
                        var snap = ProfilerGetJson();
                        ProfilerSaves[path] = snap;
                        try
                        {
                            var full = path.Replace('\\', '/');
                            if (!Path.IsPathRooted(full))
                            {
                                var root = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
                                full = Path.Combine(root, path);
                            }
                            var dir = Path.GetDirectoryName(full);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            File.WriteAllText(full, snap);
                        }
                        catch { /* in-memory save still held */ }
                        return Ok(id, "null");
                    }
                    case "profiler.load":
                    {
                        var path = ExtractString(line, "path") ?? "";
                        if (ProfilerSaves.TryGetValue(path, out var snap))
                            return Ok(id, snap);
                        try
                        {
                            var full = path;
                            if (!Path.IsPathRooted(full))
                            {
                                var root = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
                                full = Path.Combine(root, path);
                            }
                            if (File.Exists(full))
                                return Ok(id, File.ReadAllText(full));
                        }
                        catch { /* fall through */ }
                        return Ok(id, "null");
                    }
                    case "ui.query":
                        return Ok(id, QueryUiJson(ExtractString(line, "filter")));
                    case "input.simulate":
                    {
                        // Honest non-support: never ok:true empty shell.
                        var action = ExtractString(line, "action") ?? "unknown";
                        return Fail(id, "input.simulate is not implemented for real input injection. " +
                                        "Use selection_manage or menu_manage. action=" + action);
                    }
                    case "tests.run":
                        return HandleTestsRun(id, line);
                    case "tests.status":
                        return HandleTestsStatus(id, ExtractString(line, "jobId"));
                    case "tests.list":
                        return HandleTestsList(id, ExtractString(line, "mode"));
                    case "lease.get":
                        return Ok(id, SerializeLease());
                    case "lease.acquire":
                    {
                        var agentId = ExtractString(line, "agentId") ?? "";
                        var ttl = ExtractInt(line, "ttlSeconds") ?? 60;
                        if (string.IsNullOrEmpty(agentId))
                            return Fail(id, "agentId required");
                        PurgeLeaseIfExpired();
                        if (!string.IsNullOrEmpty(_leaseHolder) &&
                            !string.Equals(_leaseHolder, agentId, StringComparison.OrdinalIgnoreCase))
                            return Fail(id, "busy holder=" + _leaseHolder);
                        _leaseHolder = agentId;
                        _leaseExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(1, ttl));
                        return Ok(id, SerializeLease());
                    }
                    case "lease.release":
                    {
                        var agentId = ExtractString(line, "agentId") ?? "";
                        PurgeLeaseIfExpired();
                        if (string.IsNullOrEmpty(_leaseHolder))
                            return Fail(id, "no lease held");
                        if (!string.Equals(_leaseHolder, agentId, StringComparison.OrdinalIgnoreCase))
                            return Fail(id, "not_holder holder=" + _leaseHolder);
                        _leaseHolder = null;
                        _leaseExpiresUtc = default(DateTime);
                        return Ok(id, "true");
                    }
                    default:
                        return Fail(id, "Unknown method: " + method);
                }
            }
            catch (StaleReferenceException ex)
            {
                return Fail(id, ex.Message);
            }
            catch (Exception ex)
            {
                return Fail(id, ex.Message);
            }
        }

        // --- helpers ---

        private sealed class StaleReferenceException : Exception
        {
            public StaleReferenceException(string message) : base(message) { }
        }

        private static string FormatStaleReference(string idOrPath) =>
            "stale_reference: GameObject id '" + idOrPath +
            "' is invalid after domain reload (sessionGeneration=" + GetSessionGeneration() +
            "). Re-find by hierarchy path, then retry with the new id.";

        private static GameObject FindGo(string idOrPath)
        {
            if (string.IsNullOrEmpty(idOrPath)) return null;
            if (int.TryParse(idOrPath, out var instanceId))
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                if (obj != null) return obj;
                // Numeric instance id that no longer resolves — accuracy accident if we fall through to name search.
                throw new StaleReferenceException(FormatStaleReference(idOrPath));
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
                // Bounded property export via SerializedObject (not empty properties:{}).
                comps.Add(SerializeComponentData(c, maxProps: 24));
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
                + "\"components\":[" + string.Join(",", comps) + "],"
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
            // Supports scalar + Vector3-like nested objects {"x":..,"y":..,"z":..}
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
                else if (propsJson[valStart] == '{')
                {
                    var d = 0;
                    var j = valStart;
                    for (; j < propsJson.Length; j++)
                    {
                        if (propsJson[j] == '{') d++;
                        else if (propsJson[j] == '}')
                        {
                            d--;
                            if (d == 0) { j++; break; }
                        }
                    }
                    valToken = propsJson.Substring(valStart, j - valStart);
                    valEnd = j;
                }
                else if (propsJson[valStart] == '[')
                {
                    pos = q2 + 1;
                    continue; // arrays not applied (honest limit)
                }
                else
                {
                    while (valEnd < propsJson.Length && propsJson[valEnd] != ',' && propsJson[valEnd] != '}')
                        valEnd++;
                    valToken = propsJson.Substring(valStart, valEnd - valStart).Trim();
                }
                var sp = so.FindProperty(propName) ?? so.FindProperty("m_" + propName);
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
                    else if (sp.propertyType == SerializedPropertyType.Enum && int.TryParse(valToken, out var ei))
                    { sp.enumValueIndex = ei; applied++; }
                    else if (sp.propertyType == SerializedPropertyType.Vector3 && valToken.Length > 0 && valToken[0] == '{')
                    {
                        var vx = ExtractFloatFromObject(valToken, "x") ?? sp.vector3Value.x;
                        var vy = ExtractFloatFromObject(valToken, "y") ?? sp.vector3Value.y;
                        var vz = ExtractFloatFromObject(valToken, "z") ?? sp.vector3Value.z;
                        sp.vector3Value = new Vector3(vx, vy, vz);
                        applied++;
                    }
                    else if (sp.propertyType == SerializedPropertyType.Vector2 && valToken.Length > 0 && valToken[0] == '{')
                    {
                        var vx = ExtractFloatFromObject(valToken, "x") ?? sp.vector2Value.x;
                        var vy = ExtractFloatFromObject(valToken, "y") ?? sp.vector2Value.y;
                        sp.vector2Value = new Vector2(vx, vy);
                        applied++;
                    }
                    else if (sp.propertyType == SerializedPropertyType.Color && valToken.Length > 0 && valToken[0] == '{')
                    {
                        var cr = ExtractFloatFromObject(valToken, "r") ?? sp.colorValue.r;
                        var cg = ExtractFloatFromObject(valToken, "g") ?? sp.colorValue.g;
                        var cb = ExtractFloatFromObject(valToken, "b") ?? sp.colorValue.b;
                        var ca = ExtractFloatFromObject(valToken, "a") ?? sp.colorValue.a;
                        sp.colorValue = new Color(cr, cg, cb, ca);
                        applied++;
                    }
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

        private static string SerializeComponentData(Component c, int maxProps = 64)
        {
            if (c == null) return "null";
            var typeName = c.GetType().Name;
            var parts = new List<string>();
            try
            {
                var so = new SerializedObject(c);
                var it = so.GetIterator();
                var enter = true;
                var n = 0;
                while (it.NextVisible(enter) && n < maxProps)
                {
                    enter = false;
                    if (it.name == "m_Script" || it.name == "m_ObjectHideFlags") continue;
                    var key = it.name.StartsWith("m_", StringComparison.Ordinal) && it.name.Length > 2
                        ? char.ToLowerInvariant(it.name[2]) + it.name.Substring(3)
                        : it.name;
                    parts.Add(JsonString(key) + ":" + SerializeSerializedProp(it));
                    n++;
                }
            }
            catch
            {
                // leave properties empty only on failure
            }
            return "{\"typeName\":" + JsonString(typeName) + ",\"properties\":{" + string.Join(",", parts) + "}}";
        }

        private static string SerializeSerializedProp(SerializedProperty p)
        {
            try
            {
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Integer: return p.intValue.ToString();
                    case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                    case SerializedPropertyType.Float: return F(p.floatValue);
                    case SerializedPropertyType.String: return JsonString(p.stringValue ?? "");
                    case SerializedPropertyType.Enum: return JsonString(p.enumDisplayNames != null && p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length ? p.enumDisplayNames[p.enumValueIndex] : p.intValue.ToString());
                    case SerializedPropertyType.Vector2:
                        return "{\"x\":" + F(p.vector2Value.x) + ",\"y\":" + F(p.vector2Value.y) + "}";
                    case SerializedPropertyType.Vector3:
                        return "{\"x\":" + F(p.vector3Value.x) + ",\"y\":" + F(p.vector3Value.y) + ",\"z\":" + F(p.vector3Value.z) + "}";
                    case SerializedPropertyType.Color:
                        return "{\"r\":" + F(p.colorValue.r) + ",\"g\":" + F(p.colorValue.g) + ",\"b\":" + F(p.colorValue.b) + ",\"a\":" + F(p.colorValue.a) + "}";
                    case SerializedPropertyType.ObjectReference:
                        return p.objectReferenceValue == null ? "null" : JsonString(p.objectReferenceValue.name);
                    default:
                        return JsonString(p.propertyType.ToString());
                }
            }
            catch
            {
                return "null";
            }
        }

        private static string ListComponentTypesJson(string filter)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var t in types)
                {
                    try
                    {
                        if (t == null || t.IsAbstract || !typeof(Component).IsAssignableFrom(t)) continue;
                        if (!string.IsNullOrEmpty(filter) &&
                            t.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                            (t.FullName == null || t.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0))
                            continue;
                        names.Add(t.Name);
                    }
                    catch { /* skip */ }
                }
            }
            var list = new List<string>(names);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            if (list.Count > 500) list = list.GetRange(0, 500);
            var parts = new List<string>();
            foreach (var n in list) parts.Add(JsonString(n));
            return "[" + string.Join(",", parts) + "]";
        }

        private static string QueryUiJson(string filter)
        {
            // Real enumeration of Canvas / RectTransform UI tree (no empty stub without scanning).
            var parts = new List<string>();
            var seen = new HashSet<int>();
            try
            {
                var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
                foreach (var canvas in canvases)
                {
                    if (canvas == null) continue;
                    foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true))
                    {
                        if (rt == null) continue;
                        var go = rt.gameObject;
                        var iid = go.GetInstanceID();
                        if (!seen.Add(iid)) continue;
                        var name = go.name;
                        if (!string.IsNullOrEmpty(filter) &&
                            name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                            iid.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        var kind = "RectTransform";
                        foreach (var c in go.GetComponents<Component>())
                        {
                            if (c == null || c is RectTransform || c is Transform || c is CanvasRenderer) continue;
                            kind = c.GetType().Name;
                            break;
                        }
                        var rect = "{\"x\":" + F(rt.rect.x) + ",\"y\":" + F(rt.rect.y) +
                                   ",\"w\":" + F(rt.rect.width) + ",\"h\":" + F(rt.rect.height) + "}";
                        parts.Add("{\"id\":" + JsonString(iid.ToString()) +
                                  ",\"name\":" + JsonString(name) +
                                  ",\"kind\":" + JsonString(kind) +
                                  ",\"interactable\":true" +
                                  ",\"path\":" + JsonString(GetHierarchyPath(go)) +
                                  ",\"rect\":" + rect + "}");
                        if (parts.Count >= 200) break;
                    }
                    if (parts.Count >= 200) break;
                }
            }
            catch (Exception ex)
            {
                return "[{\"id\":\"error\",\"name\":" + JsonString(ex.Message) + ",\"kind\":\"error\",\"interactable\":false,\"rect\":{\"x\":0,\"y\":0,\"w\":0,\"h\":0}}]";
            }
            return "[" + string.Join(",", parts) + "]";
        }

        private static string GetHierarchyPath(GameObject go)
        {
            var stack = new Stack<string>();
            var t = go.transform;
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", stack.ToArray());
        }

        private static string HandleTestsRun(string id, string line)
        {
            var mode = ExtractString(line, "mode") ?? "EditMode";
            var filter = ExtractString(line, "filter");
            var jobId = Guid.NewGuid().ToString("N").Substring(0, 12);
            var job = new BridgeTestJob
            {
                JobId = jobId,
                Status = "running",
                Kind = "run",
                Mode = mode,
                Filter = filter
            };
            TestJobs[jobId] = job;
            try
            {
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                var testMode = mode.IndexOf("Play", StringComparison.OrdinalIgnoreCase) >= 0
                    ? TestMode.PlayMode
                    : TestMode.EditMode;
                var filterObj = new Filter { testMode = testMode };
                if (!string.IsNullOrEmpty(filter))
                    filterObj.testNames = new[] { filter };
                var callbacks = new BridgeTestCallbacks(jobId);
                api.RegisterCallbacks(callbacks);
                api.Execute(new ExecutionSettings(filterObj));
                return Ok(id, "{\"jobId\":" + JsonString(jobId) +
                              ",\"status\":\"running\",\"kind\":\"run\",\"mode\":" + JsonString(mode) +
                              ",\"filter\":" + (filter == null ? "null" : JsonString(filter)) +
                              ",\"note\":" + JsonString("TestRunnerApi.Execute started") + "}");
            }
            catch (Exception ex)
            {
                job.Status = "failed";
                job.Note = ex.Message;
                return Fail(id, "TestRunnerApi failed: " + ex.Message);
            }
        }

        private static string HandleTestsStatus(string id, string jobId)
        {
            if (string.IsNullOrEmpty(jobId) || !TestJobs.TryGetValue(jobId, out var job))
                return Fail(id, "unknown jobId");
            if (string.Equals(job.Status, "failed", StringComparison.OrdinalIgnoreCase))
                return Fail(id, job.Note ?? ("test job failed: " + jobId));
            var results = new List<string>();
            foreach (var line in job.ResultLines)
                results.Add(line);
            // list jobs: results are catalog entries {name,mode}; run jobs: {name,status,message}
            return Ok(id, "{\"jobId\":" + JsonString(job.JobId) +
                          ",\"status\":" + JsonString(job.Status) +
                          ",\"kind\":" + JsonString(job.Kind ?? "run") +
                          ",\"mode\":" + JsonString(job.Mode) +
                          ",\"filter\":" + (job.Filter == null ? "null" : JsonString(job.Filter)) +
                          ",\"passed\":" + (job.Passed.HasValue ? (job.Passed.Value ? "true" : "false") : "null") +
                          ",\"results\":[" + string.Join(",", results) + "]" +
                          ",\"tests\":[" + string.Join(",", results) + "]" +
                          ",\"note\":" + (job.Note == null ? "null" : JsonString(job.Note)) + "}");
        }

        /// <summary>
        /// Non-blocking RetrieveTestList: returns jobId immediately; callback fills TestJobs; poll tests.status.
        /// Never ManualResetEvent.Wait on the main thread.
        /// </summary>
        private static string HandleTestsList(string id, string mode)
        {
            var jobId = Guid.NewGuid().ToString("N").Substring(0, 12);
            var modeLabel = !string.IsNullOrEmpty(mode) && mode.IndexOf("Play", StringComparison.OrdinalIgnoreCase) >= 0
                ? "PlayMode"
                : "EditMode";
            var job = new BridgeTestJob
            {
                JobId = jobId,
                Status = "running",
                Kind = "list",
                Mode = modeLabel
            };
            TestJobs[jobId] = job;
            try
            {
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                var testMode = modeLabel == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;
                api.RetrieveTestList(testMode, adaptor =>
                {
                    try
                    {
                        var names = new List<string>();
                        CollectTestNames(adaptor, names, 200);
                        foreach (var n in names)
                        {
                            job.ResultLines.Add("{\"name\":" + JsonString(n) +
                                                ",\"mode\":" + JsonString(modeLabel) + "}");
                        }
                        job.Status = "completed";
                        job.Note = "RetrieveTestList completed count=" + names.Count;
                    }
                    catch (Exception ex)
                    {
                        job.Status = "failed";
                        job.Note = ex.Message;
                    }
                });
                return Ok(id, "{\"jobId\":" + JsonString(jobId) +
                              ",\"status\":\"running\",\"kind\":\"list\",\"mode\":" + JsonString(modeLabel) + "}");
            }
            catch (Exception ex)
            {
                job.Status = "failed";
                job.Note = ex.Message;
                return Fail(id, "TestRunnerApi list failed to start: " + ex.Message);
            }
        }

        private static void CollectTestNames(ITestAdaptor node, List<string> names, int max)
        {
            if (node == null || names.Count >= max) return;
            if (!node.IsSuite && !string.IsNullOrEmpty(node.FullName))
                names.Add(node.FullName);
            if (node.Children == null) return;
            foreach (var child in node.Children)
            {
                CollectTestNames(child, names, max);
                if (names.Count >= max) break;
            }
        }

        private sealed class BridgeTestCallbacks : ICallbacks
        {
            private readonly string _jobId;
            public BridgeTestCallbacks(string jobId) { _jobId = jobId; }

            public void RunStarted(ITestAdaptor tests) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                if (!TestJobs.TryGetValue(_jobId, out var job)) return;
                job.Status = "completed";
                job.Passed = result != null && result.TestStatus == TestStatus.Passed;
                if (result != null)
                    job.Note = "resultStatus=" + result.TestStatus;
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!TestJobs.TryGetValue(_jobId, out var job) || result == null) return;
                if (result.Test != null && result.Test.IsSuite) return;
                var name = result.Test != null ? result.Test.FullName : "test";
                var status = result.TestStatus.ToString();
                var msg = result.Message ?? "";
                job.ResultLines.Add("{\"name\":" + JsonString(name) +
                                    ",\"status\":" + JsonString(status) +
                                    ",\"message\":" + JsonString(msg) + "}");
            }
        }

        /// <summary>
        /// Start UPM Client request and return immediately with jobId (status=running).
        /// Completion is polled via package.status + EditorApplication.update (no main-thread Sleep).
        /// </summary>
        private static string StartPackageJob(string id, string op, string packageId, string query)
        {
            try
            {
                Request req;
                switch (op)
                {
                    case "list":
                        req = Client.List(true, true);
                        break;
                    case "add":
                        if (string.IsNullOrEmpty(packageId))
                            return Fail(id, "package id required for package.add");
                        req = Client.Add(packageId);
                        break;
                    case "remove":
                        if (string.IsNullOrEmpty(packageId))
                            return Fail(id, "package name required for package.remove");
                        req = Client.Remove(packageId);
                        break;
                    case "search":
                        req = string.IsNullOrWhiteSpace(query) ? Client.SearchAll() : Client.Search(query);
                        break;
                    default:
                        return Fail(id, "unknown package op: " + op);
                }

                var jobId = Guid.NewGuid().ToString("N").Substring(0, 12);
                var job = new BridgePackageJob
                {
                    JobId = jobId,
                    Status = "running",
                    Op = op,
                    Request = req,
                    PackageId = packageId,
                    Query = query
                };
                lock (PackageJobs)
                {
                    PackageJobs[jobId] = job;
                }
                // If already completed by the time we return (cached), pump once.
                PumpPackageJobs();
                return Ok(id, "{\"jobId\":" + JsonString(jobId) +
                              ",\"status\":" + JsonString(job.Status) +
                              ",\"op\":" + JsonString(op) + "}");
            }
            catch (Exception ex)
            {
                return Fail(id, "PackageManager." + op + " failed to start: " + ex.Message);
            }
        }

        private static string HandlePackageStatus(string id, string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return Fail(id, "jobId required");
            BridgePackageJob job;
            lock (PackageJobs)
            {
                if (!PackageJobs.TryGetValue(jobId, out job))
                    return Fail(id, "unknown package jobId: " + jobId);
            }
            // Opportunistic pump in case update lag.
            PumpPackageJobs();
            if (job.Status == "running")
            {
                return Ok(id, "{\"jobId\":" + JsonString(job.JobId) +
                              ",\"status\":\"running\",\"op\":" + JsonString(job.Op ?? "") + "}");
            }
            if (job.Status == "failed")
            {
                // Honest Fail — never Ok with a fake packages[] entry named "error".
                return Fail(id, "package." + job.Op + " failed: " + (job.Error ?? "unknown error"));
            }
            // completed
            if (job.Op == "list" || job.Op == "search")
            {
                return Ok(id, "{\"jobId\":" + JsonString(job.JobId) +
                              ",\"status\":\"completed\",\"op\":" + JsonString(job.Op) +
                              ",\"packages\":" + (job.ResultJson ?? "[]") + "}");
            }
            if (job.Op == "add")
            {
                return Ok(id, "{\"jobId\":" + JsonString(job.JobId) +
                              ",\"status\":\"completed\",\"op\":\"add\",\"package\":" +
                              (job.ResultJson ?? "null") + "}");
            }
            if (job.Op == "remove")
            {
                return Ok(id, "{\"jobId\":" + JsonString(job.JobId) +
                              ",\"status\":\"completed\",\"op\":\"remove\",\"removed\":" +
                              (job.ResultJson ?? "false") + "}");
            }
            return Fail(id, "package job completed with unknown op");
        }

        private static string ListMenusJson(string filter)
        {
            // Whitelist coverage only — declared LIMITED, not a full menu tree.
            var parts = new List<string>();
            foreach (var path in BuiltinMenuCatalog)
            {
                if (!string.IsNullOrEmpty(filter) && path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var cat = path.Contains("/") ? path.Substring(0, path.IndexOf('/')) : path;
                parts.Add("{\"path\":" + JsonString(path) + ",\"category\":" + JsonString(cat) +
                          ",\"coverage\":\"whitelist\"}");
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

        // Ivan screenshot-isolated: transient user layer for culling (restored in finally).
        private const int IsolationLayer = 31;

        /// <summary>
        /// Coplay-inspired capture semantics (algorithm port from ScreenshotUtility / EditorWindowScreenshotUtility):
        /// - game_view without target → ScreenCapture composited path (Overlay UI included)
        /// - camera / explicit target → camera.Render (overlay UI excluded)
        /// - isolated → Ivan-style temp layer + staging camera looking only at target GO (+children)
        /// - scene_view → SceneView GrabPixels (or explicit error)
        /// - batch=surround → ONE labeled 6-view contact sheet (AC-V7)
        /// - whole-frame longest-edge downscale (default 640 cost knob); region crops stay native (AC-V9)
        /// Throws on failure — never returns a fake success marker.
        /// </summary>
        private static string CaptureScreenshotJson(
            string source,
            string targetId,
            int width,
            int height,
            int maxResolution,
            int? regionX,
            int? regionY,
            int? regionWidth,
            int? regionHeight,
            string batch)
        {
            width = Math.Max(16, Math.Min(width, 4096));
            height = Math.Max(16, Math.Min(height, 4096));
            if (maxResolution <= 0) maxResolution = 640;
            bool hasRegion = regionX.HasValue && regionY.HasValue && regionWidth.HasValue && regionHeight.HasValue
                             && regionWidth.Value > 0 && regionHeight.Value > 0;
            string src = (source ?? "game_view").Trim().ToLowerInvariant();
            string batchMode = string.IsNullOrWhiteSpace(batch) ? "none" : batch.Trim().ToLowerInvariant();
            bool hasExplicitTarget = !string.IsNullOrEmpty(targetId);

            Texture2D tex = null;
            Texture2D working = null;
            Texture2D downscaled = null;
            bool? overlayUiIncluded = null;
            bool regionNative = false;
            bool wholeFrameDownscaled = false;
            string note;

            try
            {
                if (batchMode == "surround")
                {
                    if (!hasExplicitTarget)
                        throw new InvalidOperationException(
                            "batch=surround requires targetId (GameObject to orbit). Returns ONE labeled contact sheet.");
                    var tile = Math.Max(64, Math.Min(width, 320));
                    tex = CaptureSurroundContactSheet(targetId, tile, tile, out note);
                    overlayUiIncluded = false;
                    src = string.IsNullOrEmpty(src) ? "isolated" : src;
                }
                else if (src == "scene_view")
                {
                    tex = CaptureSceneViewTexture();
                    overlayUiIncluded = false;
                    note = "Scene View viewport grab (GrabPixels). Overlay game UI not included.";
                }
                else if (src == "game_view" && !hasExplicitTarget)
                {
                    // Composited path — includes Screen Space Overlay UI (Coplay CaptureComposited).
                    tex = CaptureCompositedTexture(1);
                    if (tex == null)
                    {
                        var fallback = FindAvailableCamera(null);
                        if (fallback == null)
                            throw new InvalidOperationException(
                                "game_view capture failed: ScreenCapture returned null and no Camera is available. " +
                                "Add a Camera to the scene or open Game View with a rendered frame.");
                        tex = RenderCameraToTexture(fallback, width, height);
                        overlayUiIncluded = false;
                        note = "game_view fell back to camera.Render (ScreenCapture unavailable); Overlay UI excluded.";
                    }
                    else
                    {
                        overlayUiIncluded = true;
                        note = "game_view composited via ScreenCapture.CaptureScreenshotAsTexture; Overlay UI included.";
                    }
                }
                else if (src == "isolated")
                {
                    if (!hasExplicitTarget)
                        throw new InvalidOperationException(
                            "source=isolated requires targetId (GameObject instance id, name, or hierarchy path).");
                    tex = CaptureIsolatedObjectTexture(targetId, width, height, out note);
                    overlayUiIncluded = false;
                }
                else if (src == "camera" || src == "game_view")
                {
                    var cam = FindAvailableCamera(targetId);
                    if (cam == null)
                        throw new InvalidOperationException(
                            "No Camera available for source=" + src +
                            (hasExplicitTarget ? (" target=" + targetId) : "") +
                            ". Add a Camera or pass target=<cameraGameObjectId>.");
                    tex = RenderCameraToTexture(cam, width, height);
                    overlayUiIncluded = false;
                    note = "camera.Render path; Screen Space – Overlay UI excluded.";
                }
                else
                {
                    throw new InvalidOperationException(
                        "Unknown screenshot source '" + source + "'. Use camera|game_view|scene_view|isolated.");
                }

                if (tex == null)
                    throw new InvalidOperationException("Screenshot capture produced no texture.");

                working = tex;
                tex = null;

                if (hasRegion && batchMode != "surround")
                {
                    // AC-V9: region crops stay at native resolution — never apply 640 downscale.
                    working = CropTextureTopLeft(working, regionX.Value, regionY.Value, regionWidth.Value, regionHeight.Value, destroySource: true);
                    regionNative = true;
                    note += " Region crop at native resolution (maxResolution cost knob not applied).";
                }
                else if (batchMode != "surround")
                {
                    // Whole-frame: longest-edge downscale (Coplay DownscaleTexture, default 640 cost knob).
                    if (working.width > maxResolution || working.height > maxResolution)
                    {
                        downscaled = DownscaleTexture(working, maxResolution);
                        UnityEngine.Object.DestroyImmediate(working);
                        working = downscaled;
                        downscaled = null;
                        wholeFrameDownscaled = true;
                        note += " Whole-frame downscaled to maxResolution=" + maxResolution + " (cost knob).";
                    }
                }
                else
                {
                    note += " Contact sheet whole-frame; tile size controlled by width (capped).";
                    if (working.width > maxResolution * 2 || working.height > maxResolution * 2)
                    {
                        downscaled = DownscaleTexture(working, maxResolution * 2);
                        UnityEngine.Object.DestroyImmediate(working);
                        working = downscaled;
                        downscaled = null;
                        wholeFrameDownscaled = true;
                    }
                }

                byte[] png = working.EncodeToPNG();
                int outW = working.width;
                int outH = working.height;
                string b64 = Convert.ToBase64String(png);

                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
                var outDir = Path.Combine(projectRoot, "Temp");
                Directory.CreateDirectory(outDir);
                var filePath = Path.Combine(outDir, "unity-comdr-shot-" + Guid.NewGuid().ToString("N") + ".png")
                    .Replace('\\', '/');
                File.WriteAllBytes(filePath, png);

                return "{"
                    + "\"source\":" + JsonString(src) + ","
                    + "\"format\":\"png\","
                    + "\"note\":" + JsonString(note) + ","
                    + "\"width\":" + outW + ","
                    + "\"height\":" + outH + ","
                    + "\"filePath\":" + JsonString(filePath) + ","
                    + "\"pngBase64\":" + JsonString(b64) + ","
                    + "\"isRealPixels\":true,"
                    + "\"batch\":" + JsonString(batchMode) + ","
                    + "\"regionNative\":" + (regionNative ? "true" : "false") + ","
                    + "\"wholeFrameDownscaled\":" + (wholeFrameDownscaled ? "true" : "false") + ","
                    + "\"overlayUiIncluded\":" + (overlayUiIncluded == null ? "null" : (overlayUiIncluded.Value ? "true" : "false"))
                    + (hasExplicitTarget ? ",\"targetId\":" + JsonString(targetId) : "")
                    + "}";
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (working != null) UnityEngine.Object.DestroyImmediate(working);
                if (downscaled != null) UnityEngine.Object.DestroyImmediate(downscaled);
            }
        }

        /// <summary>
        /// AC-V7: six yaw angles around target → one labeled contact sheet (not N separate images).
        /// </summary>
        private static Texture2D CaptureSurroundContactSheet(string targetId, int tileW, int tileH, out string note)
        {
            var go = FindGo(targetId);
            if (go == null)
                throw new InvalidOperationException("surround target not found: " + targetId);

            var renderers = new List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r != null) renderers.Add(r);
            var bounds = ComputeRendererBounds(renderers);
            if (bounds.size.sqrMagnitude < 1e-8f)
                bounds = new Bounds(go.transform.position, Vector3.one);

            var labels = new[] { "Front", "Back", "Left", "Right", "Top", "Bottom-ish" };
            var dirs = new[]
            {
                new Vector3(0f, 0f, -1f),
                new Vector3(0f, 0f, 1f),
                new Vector3(-1f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(0.35f, -1f, 0.35f)
            };

            var tiles = new List<Texture2D>(6);
            var temps = new List<UnityEngine.Object>();
            try
            {
                for (var i = 0; i < dirs.Length; i++)
                {
                    var camGo = new GameObject("__UnityComdr_SurroundCam_" + i)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    temps.Add(camGo);
                    var cam = camGo.AddComponent<Camera>();
                    cam.enabled = false;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);
                    cam.fieldOfView = 40f;
                    cam.nearClipPlane = 0.01f;
                    cam.farClipPlane = 1000f;
                    FrameCameraOnBounds(cam, bounds, 1.35f);
                    // Override position along orbit direction
                    var radius = bounds.extents.magnitude;
                    if (radius < 0.05f) radius = 0.5f;
                    var dist = radius * 2.2f;
                    var dir = dirs[i].normalized;
                    cam.transform.position = bounds.center - dir * dist;
                    cam.transform.LookAt(bounds.center, Vector3.up);

                    var tile = RenderCameraToTexture(cam, tileW, tileH);
                    // Label strip via note only — pixel labels: draw solid bar is heavy; composite grid is enough
                    tiles.Add(tile);
                }

                var atlas = ComposeContactSheet(tiles, 3, 2, labels);
                note = "AC-V7 surround contact sheet: 6 labeled angles (Front/Back/Left/Right/Top/Bottom-ish) in ONE image. " +
                       "Overlay UI excluded (orbit cameras).";
                return atlas;
            }
            finally
            {
                foreach (var t in tiles)
                {
                    if (t != null) UnityEngine.Object.DestroyImmediate(t);
                }
                foreach (var o in temps)
                {
                    if (o != null) UnityEngine.Object.DestroyImmediate(o);
                }
            }
        }

        private static Texture2D ComposeContactSheet(List<Texture2D> tiles, int cols, int rows, string[] labels)
        {
            if (tiles == null || tiles.Count == 0)
                throw new InvalidOperationException("contact sheet needs tiles");
            var tileW = tiles[0].width;
            var tileH = tiles[0].height;
            var labelH = 18;
            var atlas = new Texture2D(cols * tileW, rows * (tileH + labelH), TextureFormat.RGBA32, false);
            var clear = new Color(0.08f, 0.08f, 0.1f, 1f);
            var pixels = atlas.GetPixels();
            for (var i = 0; i < pixels.Length; i++) pixels[i] = clear;
            atlas.SetPixels(pixels);

            for (var i = 0; i < tiles.Count && i < cols * rows; i++)
            {
                var col = i % cols;
                var row = i / cols;
                // Unity tex coords: y from bottom
                var destX = col * tileW;
                var destY = (rows - 1 - row) * (tileH + labelH);
                var src = tiles[i];
                if (src == null) continue;
                // label bar (solid) above tile in sheet space
                var barY = destY + tileH;
                for (var y = 0; y < labelH; y++)
                    for (var x = 0; x < tileW; x++)
                        atlas.SetPixel(destX + x, barY + y, new Color(0.2f, 0.25f, 0.35f, 1f));
                // crude label: brighter strip encodes index (full text needs font — index band)
                var labelFrac = labels != null && i < labels.Length ? (i + 1) / (float)(labels.Length + 1) : 0.5f;
                for (var x = 0; x < (int)(tileW * labelFrac); x++)
                    for (var y = 2; y < labelH - 2; y++)
                        atlas.SetPixel(destX + x, barY + y, new Color(0.85f, 0.9f, 1f, 1f));

                var tp = src.GetPixels();
                atlas.SetPixels(destX, destY, src.width, src.height, tp);
            }
            atlas.Apply();
            return atlas;
        }

        private static Camera FindAvailableCamera(string targetId)
        {
            if (!string.IsNullOrEmpty(targetId))
            {
                var go = FindGo(targetId);
                if (go != null)
                {
                    var c = go.GetComponent<Camera>();
                    if (c != null) return c;
                }
            }
            if (Camera.main != null) return Camera.main;
            return UnityEngine.Object.FindObjectOfType<Camera>();
        }

        /// <summary>
        /// Ivan-style isolated capture (algorithm port from Screenshot.Isolated):
        /// temporarily assign target (+children) to IsolationLayer, stage a temp camera+light
        /// that culls only that layer, Render once, restore layers/activeSelf, destroy temps.
        /// </summary>
        private static Texture2D CaptureIsolatedObjectTexture(string targetId, int width, int height, out string note)
        {
            note = null;
            var target = FindGo(targetId);
            if (target == null)
                throw new InvalidOperationException(
                    "isolated capture failed: GameObject not found for targetId='" + targetId + "'.");

            var renderers = new List<Renderer>();
            target.GetComponentsInChildren(true, renderers);
            if (renderers.Count == 0)
                throw new InvalidOperationException(
                    "isolated capture failed: no Renderer on target or children ('" + target.name + "').");

            var bounds = ComputeRendererBounds(renderers);
            var targets = CollectIsolationGameObjects(target);
            var originalLayers = new Dictionary<GameObject, int>(targets.Count);
            var originalActiveSelf = new Dictionary<GameObject, bool>(targets.Count);
            var temporaryObjects = new List<GameObject>(2);
            RenderTexture rt = null;
            Texture2D tex = null;
            var prevActive = RenderTexture.active;
            var activatedInactive = false;

            try
            {
                foreach (var go in targets)
                {
                    if (go == null) continue;
                    originalLayers[go] = go.layer;
                    originalActiveSelf[go] = go.activeSelf;
                    if (!go.activeSelf)
                    {
                        go.SetActive(true);
                        activatedInactive = true;
                    }
                    go.layer = IsolationLayer;
                }

                int w = Math.Max(16, width);
                int h = Math.Max(16, height);
                rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);

                var camGo = new GameObject("__UnityComdr_IsolationCamera__")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                temporaryObjects.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 1000f;
                cam.cullingMask = 1 << IsolationLayer;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.targetTexture = rt;
                FrameCameraOnBounds(cam, bounds, padding: 1.2f);

                var lightGo = new GameObject("__UnityComdr_IsolationLight__")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                temporaryObjects.Add(lightGo);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1f;
                light.cullingMask = 1 << IsolationLayer;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();

                var result = tex;
                tex = null;

                note = "Ivan-style isolated: temp layer " + IsolationLayer
                    + " + staging camera/light culling only target (+children); layers/activeSelf restored after render."
                    + " Limitations: Screen Space Overlay UI excluded; inactive children briefly SetActive(true)"
                    + " so OnEnable side effects are not rewindable; single Front view here — use batch=surround for 6-angle contact sheet;"
                    + " layer " + IsolationLayer
                    + " is borrowed only for the capture window."
                    + (activatedInactive ? " Some inactive children were temporarily activated." : "");
                return result;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                foreach (var go in temporaryObjects)
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }
                foreach (var kvp in originalLayers)
                {
                    if (kvp.Key != null) kvp.Key.layer = kvp.Value;
                }
                foreach (var kvp in originalActiveSelf)
                {
                    if (kvp.Key != null && kvp.Key.activeSelf != kvp.Value)
                        kvp.Key.SetActive(kvp.Value);
                }
            }
        }

        private static List<GameObject> CollectIsolationGameObjects(GameObject target)
        {
            var list = new List<GameObject> { target };
            var transforms = target.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var go = transforms[i] != null ? transforms[i].gameObject : null;
                if (go != null && go != target)
                    list.Add(go);
            }
            return list;
        }

        private static Bounds ComputeRendererBounds(List<Renderer> renderers)
        {
            var initialised = false;
            var bounds = new Bounds();
            for (var i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                if (!initialised)
                {
                    bounds = r.bounds;
                    initialised = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
            if (!initialised || bounds.size == Vector3.zero)
                bounds = new Bounds(bounds.center, Vector3.one * 0.1f);
            return bounds;
        }

        private static void FrameCameraOnBounds(Camera cam, Bounds bounds, float padding)
        {
            var radius = bounds.extents.magnitude;
            if (radius < 0.0001f) radius = 0.05f;
            var fovRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            var distance = (radius * padding) / Mathf.Sin(fovRad);
            // Front view: camera looks along -Z toward bounds center (Ivan CameraView.Front).
            cam.transform.position = bounds.center + new Vector3(0f, 0f, -1f) * distance;
            cam.transform.LookAt(bounds.center, Vector3.up);
            cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, Mathf.Max(0.01f, distance * 0.01f));
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, distance + radius * 4f);
        }

        private static Texture2D RenderCameraToTexture(Camera camera, int width, int height)
        {
            int w = Math.Max(1, camera.pixelWidth > 0 ? camera.pixelWidth : width);
            int h = Math.Max(1, camera.pixelHeight > 0 ? camera.pixelHeight : height);
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prevRt = camera.targetTexture;
            var prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                var result = tex;
                tex = null;
                return result;
            }
            finally
            {
                camera.targetTexture = prevRt;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// Port of Coplay ScreenshotUtility.CaptureComposited (+ WaitForEndOfFrame in play mode).
        /// </summary>
        private static Texture2D CaptureCompositedTexture(int superSize)
        {
            superSize = Math.Max(1, superSize);
            try
            {
                if (Application.isPlaying)
                {
                    var afterFrame = CaptureCompositedAfterFrame(superSize);
                    if (afterFrame != null) return afterFrame;
                }
                return ScreenCapture.CaptureScreenshotAsTexture(superSize);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Unity-Comdr] CaptureScreenshotAsTexture failed: " + ex.Message);
                return null;
            }
        }

        private static Texture2D CaptureCompositedAfterFrame(int superSize, int timeoutSteps = 5)
        {
            Texture2D result = null;
            bool done = false;
            bool callerReturned = false;
            var go = new GameObject("__UnityComdr_ScreenshotCapturer__") { hideFlags = HideFlags.HideAndDontSave };
            var capturer = go.AddComponent<ScreenshotFrameCapturer>();
            capturer.Begin(superSize, tex =>
            {
                if (callerReturned)
                {
                    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                    return;
                }
                result = tex;
                done = true;
            });
            bool wasPaused = EditorApplication.isPaused;
            try
            {
                for (int i = 0; i < timeoutSteps && !done; i++)
                    EditorApplication.Step();
            }
            finally
            {
                if (!wasPaused)
                    EditorApplication.isPaused = false;
            }
            callerReturned = true;
            if (!done && go != null)
                UnityEngine.Object.DestroyImmediate(go);
            return result;
        }

        /// <summary>
        /// Scene View viewport grab via internal GUIView.GrabPixels (Coplay EditorWindowScreenshotUtility idea).
        /// </summary>
        private static Texture2D CaptureSceneViewTexture()
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                throw new InvalidOperationException(
                    "scene_view capture failed: no active Scene View. Focus a Scene View window and retry.");

            try { sceneView.Focus(); } catch { /* best effort */ }
            try
            {
                sceneView.Repaint();
                SceneView.RepaintAll();
                System.Threading.Thread.Sleep(75);
            }
            catch { /* best effort */ }

            var hostField = typeof(EditorWindow).GetField("m_Parent",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var hostView = hostField?.GetValue(sceneView);
            if (hostView == null)
                throw new InvalidOperationException(
                    "scene_view capture failed: could not resolve Scene View host view (GrabPixels unavailable).");

            var grab = hostView.GetType().GetMethod(
                "GrabPixels",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                null,
                new[] { typeof(RenderTexture), typeof(Rect) },
                null);
            if (grab == null)
                throw new InvalidOperationException(
                    "scene_view capture failed: GUIView.GrabPixels not found on this Unity version.");

            Camera cam = sceneView.camera;
            int width = cam != null && cam.pixelWidth > 0 ? cam.pixelWidth : 640;
            int height = cam != null && cam.pixelHeight > 0 ? cam.pixelHeight : 360;
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("scene_view capture failed: empty viewport.");

            var viewport = new Rect(0, 0, width, height);
            RenderTexture rt = null;
            var prevActive = RenderTexture.active;
            try
            {
                rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
                rt.Create();
                grab.Invoke(hostView, new object[] { rt, viewport });
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                FlipTextureVertically(tex);
                return tex;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "scene_view capture failed: " + (ex.InnerException ?? ex).Message, ex);
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
        }

        private static void FlipTextureVertically(Texture2D tex)
        {
            var pixels = tex.GetPixels32();
            int w = tex.width;
            int h = tex.height;
            var flipped = new Color32[pixels.Length];
            for (int y = 0; y < h; y++)
                Array.Copy(pixels, y * w, flipped, (h - 1 - y) * w, w);
            tex.SetPixels32(flipped);
            tex.Apply();
        }

        /// <summary>Port of Coplay ScreenshotUtility.DownscaleTexture (bilinear blit, never upscale).</summary>
        private static Texture2D DownscaleTexture(Texture2D source, int maxEdge)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (maxEdge <= 0) throw new ArgumentOutOfRangeException("maxEdge");

            int srcW = source.width;
            int srcH = source.height;
            float scale = Mathf.Min((float)maxEdge / srcW, (float)maxEdge / srcH);
            scale = Mathf.Min(scale, 1f);
            int dstW = Mathf.Max(1, Mathf.RoundToInt(srcW * scale));
            int dstH = Mathf.Max(1, Mathf.RoundToInt(srcH * scale));

            var prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(dstW, dstH, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var dst = new Texture2D(dstW, dstH, TextureFormat.RGBA32, false);
                dst.ReadPixels(new Rect(0, 0, dstW, dstH), 0, 0);
                dst.Apply();
                return dst;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>Crop with top-left origin (agent-friendly); converts to Unity bottom-left.</summary>
        private static Texture2D CropTextureTopLeft(Texture2D source, int x, int y, int w, int h, bool destroySource)
        {
            x = Mathf.Clamp(x, 0, Math.Max(0, source.width - 1));
            y = Mathf.Clamp(y, 0, Math.Max(0, source.height - 1));
            w = Mathf.Clamp(w, 1, source.width - x);
            h = Mathf.Clamp(h, 1, source.height - y);
            int unityY = source.height - y - h;
            if (unityY < 0) unityY = 0;
            if (unityY + h > source.height) h = source.height - unityY;

            var pixels = source.GetPixels(x, unityY, w, h);
            var cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
            cropped.SetPixels(pixels);
            cropped.Apply();
            if (destroySource) UnityEngine.Object.DestroyImmediate(source);
            return cropped;
        }

        /// <summary>Play-mode WaitForEndOfFrame helper (Coplay ScreenshotCapturer pattern).</summary>
        private sealed class ScreenshotFrameCapturer : MonoBehaviour
        {
            private int _superSize = 1;
            private Action<Texture2D> _onComplete;

            public void Begin(int superSize, Action<Texture2D> onComplete)
            {
                _superSize = Math.Max(1, superSize);
                _onComplete = onComplete;
                StartCoroutine(CaptureRoutine());
            }

            private System.Collections.IEnumerator CaptureRoutine()
            {
                yield return new WaitForEndOfFrame();
                Texture2D tex = null;
                try { tex = ScreenCapture.CaptureScreenshotAsTexture(_superSize); }
                catch (Exception ex) { Debug.LogError("[Unity-Comdr] CaptureScreenshotAsTexture failed: " + ex.Message); }
                _onComplete?.Invoke(tex);
                Destroy(gameObject);
            }
        }

        private static void PurgeLeaseIfExpired()
        {
            if (!string.IsNullOrEmpty(_leaseHolder) && DateTime.UtcNow >= _leaseExpiresUtc)
            {
                _leaseHolder = null;
                _leaseExpiresUtc = default(DateTime);
            }
        }

        private static string SerializeLease()
        {
            PurgeLeaseIfExpired();
            if (string.IsNullOrEmpty(_leaseHolder))
                return "{\"holder\":null,\"expiresAt\":null,\"held\":false}";
            return "{\"holder\":" + JsonString(_leaseHolder) +
                   ",\"expiresAt\":" + JsonString(_leaseExpiresUtc.ToString("o")) +
                   ",\"held\":true}";
        }

        private static string SerializeState()
        {
            var scene = SceneManager.GetActiveScene();
            _isCompiling = EditorApplication.isCompiling;
            var phase = CurrentPhase();
            var retry = SuggestedRetryForPhase(phase);
            return "{"
                + "\"hostMode\":\"live\","
                + "\"hostDetail\":" + JsonString("LiveUnityBridgeServer on 127.0.0.1:" + ListeningPort) + ","
                + "\"phase\":" + JsonString(phase) + ","
                + (retry.HasValue ? "\"suggestedRetrySeconds\":" + retry.Value + "," : "")
                + "\"isCompiling\":" + (_isCompiling ? "true" : "false") + ","
                + "\"isPlaying\":" + (EditorApplication.isPlaying ? "true" : "false") + ","
                + "\"isPaused\":" + (EditorApplication.isPaused ? "true" : "false") + ","
                + "\"activeScenePath\":" + JsonString(scene.path ?? "") + ","
                + "\"compileEpoch\":" + GetCompileEpoch() + ","
                + "\"sessionGeneration\":" + GetSessionGeneration()
                + "}";
        }

        private static string SerializeLogs()
        {
            lock (Gate)
            {
                var epoch = GetCompileEpoch();
                var parts = new List<string>();
                foreach (var l in Logs)
                {
                    var stale = l.Epoch < epoch ? "true" : "false";
                    parts.Add("{\"type\":" + JsonString(l.Type) +
                              ",\"message\":" + JsonString(l.Message) +
                              ",\"file\":null,\"line\":0" +
                              ",\"epoch\":" + l.Epoch +
                              ",\"stale\":" + stale + "}");
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
            public int Epoch;
        }
    }
}
#endif


