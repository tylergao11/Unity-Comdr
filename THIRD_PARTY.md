# Third-party and upstream references

Unity-Comdr is implemented primarily as **original C# code**. It does **not** currently vendor large trees from other repositories. Tool surface and architecture were informed by:

| Project | License | What we learned |
|---------|---------|-----------------|
| [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) | MIT | Focused tool entrypoints, client configure UX, multi-instance awareness (P1) |
| [IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP) | Apache-2.0 | Skills / extensibility, reflection escape hatches, domain packs |
| [CoderGamester/mcp-unity](https://github.com/CoderGamester/mcp-unity) | MIT | Resources-style compact reads, project-local config patterns |

## Borrowed algorithms / patterns (Phase V vision)

| Upstream source | Our file | Copy method |
|-----------------|----------|-------------|
| Coplay `MCPForUnity/Runtime/Helpers/ScreenshotUtility.cs` — `CaptureComposited`, `CaptureFromCamera*`, `DownscaleTexture`, `ScreenshotCapturer` (WaitForEndOfFrame) | `packages/com.unitycomdr.mcp/Editor/LiveUnityBridgeServer.cs` (`CaptureScreenshotJson` + helpers) | Algorithm port (MIT; see NOTICE) |
| Coplay `MCPForUnity/Editor/Helpers/EditorWindowScreenshotUtility.cs` — Scene View `GrabPixels` viewport grab | same (`CaptureSceneViewTexture`) | Algorithm port (ideas + reflection GrabPixels) |
| Coplay Python `manage_camera.py` — `extract_screenshot_images` → MCP `ImageContent` | `src/UnityComdr.McpHost/McpServer.cs` (`ToolsCallResult` emits `type:"image"`) + `ToolResult.OkWithImages` | Pattern port |
| IvanMurzak `Screenshot.Isolated.cs` — layer-31 isolation, temp camera/light, restore in `finally` | `packages/com.unitycomdr.mcp/Editor/LiveUnityBridgeServer.cs` (`CaptureIsolatedObjectTexture`) | Algorithm port (single Front view + default light; no Composite / lights JSON) |

## Borrowed algorithms / patterns (Phase E agent UX)

| Upstream source | Our file | Copy method |
|-----------------|----------|-------------|
| Coplay `MCPForUnity/Editor/Tools/RunTests.cs` + `GetTestJob.cs` — start returns `{job_id, status}`; poll by job id | `SampleSkills` + `LiveUnityBridgeServer` `tests.*` via `TestRunnerApi` | Pattern + live API (headless isError; no fake console/script tests) |

## Borrowed algorithms / patterns (Phase I install)

| Upstream source | Our file | Copy method |
|-----------------|----------|-------------|
| Coplay `MCPForUnity/Editor/Clients/` (Configurators + `JsonFileMcpConfigurator`) + Windows ClientConfig UI | `src/UnityComdr.Core/ClientConfig/*`, `packages/com.unitycomdr.mcp/Editor/ClientConfig/*`, `UnityComdrWindow.cs` | Pattern port (detect + write project-local mcp.json / Codex TOML; no Python/uvx) |
| Cursor / VS Code documented `mcp/install` deeplink shapes | `ClientConfigBuilder.BuildCursorDeeplink` / `BuildVsCodeDeeplink` | Pattern port (BASE64JSON / URL-encoded JSON) |
| CoderGamester project-relative auto-config idea | `ProjectConfigWriter` + relative host paths in `.cursor/mcp.json` etc. | Pattern port |

## Borrowed algorithms / patterns (Phase R resilience — main thread)

| Upstream source | Our file | Copy method |
|-----------------|----------|-------------|
| Coplay `MCPForUnity/Editor/Services/Transport/TransportCommandDispatcher.cs` — permanent `EditorApplication.update` drain, `SynchronizationContext.Post`, `QueuePlayerLoopUpdate` wake, re-entrancy flag | `packages/com.unitycomdr.mcp/Editor/LiveUnityBridgeServer.cs` (`MainThreadQueue`, `ProcessMainThreadQueue`, `RequestMainThreadPump`, `RunOnMainThread`) | Pattern port (MIT; queue + main-thread pump, not a full command registry) |
| CoderGamester `Editor/UnityBridge` — update-drained queue so requests continue while Editor unfocused | same | Pattern port (idea; no Node/WebSocket transport) |
| Coplay `StdioBridgeHost` ensure-started on Editor idle after transitions | same (`OnEditorUpdate` auto-`Start` when listener down) | Pattern port (idle re-bind only) |

## NuGet

- `System.Text.Json` — MIT (Microsoft)
- `xunit` / `Microsoft.NET.Test.Sdk` — Apache-2.0 / test only

When porting concrete upstream source files, add file-level SPDX headers and update this document.
