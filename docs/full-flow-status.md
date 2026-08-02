# Full-flow status (code-fix · scene-build · playmode-verify)

**Date:** 2026-08-02  
**Host version:** 0.2.x  
**Launch surface:** See [`docs/launch-readiness.md`](launch-readiness.md) for go/no-go.

## Three loops

| Loop | Headless (`InMemoryEditorHost`) | Live Unity (TCP bridge) |
|------|----------------------------------|-------------------------|
| **Code-fix** | **PASS** — `FullLoop_CodeFix_*` on shipped registry; write/compile clear CS errors without `console_clear` | **Code ready** — same handlers via `BridgeClientEditorHost` → `LiveUnityBridgeServer`; **E2E Editor smoke residual** until operator opens Unity with package |
| **Scene-build** | **PASS** — create GO/material/prefab + isolation | **Code ready** — root/child IDs + find filters fixed; E2E residual without Editor |
| **Playmode-verify** | **PASS** — play/pause/stop/step + screenshot skill | **Code ready** — maps to `EditorApplication`; E2E residual without Editor |

## Live bridge

| Piece | Location |
|-------|----------|
| Shared interface | `src/UnityComdr.Core/Editor/IEditorHost.cs` |
| Headless adapter | `InMemoryEditorHost` |
| Live client adapter | `BridgeClientEditorHost` |
| Host selection | `EditorHostFactory.CreateFromEnvironment()` |
| Editor server | `packages/com.unitycomdr.mcp/Editor/LiveUnityBridgeServer.cs` |
| JSON unescape (tested) | `BridgeJson.ExtractString` (+ mirrored rules in live server) |
| Default port | `17890` (`UNITY_COMDR_BRIDGE_PORT`) |
| Force headless | `UNITY_COMDR_FORCE_HEADLESS=1` |

**When Unity Editor is not running:** host uses headless mode. This is intentional and verified — not a silent fake of live output.

**When Unity Editor has the package loaded:** bridge auto-starts on domain load; MCP host prefers live connection.

## Operator full flow

1. Install UPM package `packages/com.unitycomdr.mcp`.  
2. Open Unity (bridge auto-starts; **Window → Unity-Comdr MCP**).  
3. `dotnet build -c Release`; point MCP client at `UnityComdr.McpHost.dll`.  
4. Run three loops; for playmode: `skill_manage action=load id=playmode`.  
5. CI: `UNITY_COMDR_FORCE_HEADLESS=1` + `dotnet test`.

## Tests

```bash
dotnet test UnityComdr.sln -c Release
```

Key: `FullLoopWorkflowTests`, `BridgeJsonTests`, skill budget, MCP process double-launch.
