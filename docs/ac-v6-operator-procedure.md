# AC-V6 Operator Procedure — Human vision session (residual)

**Status:** Required before product claim「agent 真看见 UI」is CLOSED.  
**Authority:** [`acceptance-criteria.md`](acceptance-criteria.md) AC-V6 · [`audit.md`](audit.md) VISION-*

## Fixture (prepare in live Unity)

1. Open a Unity project with `com.unitycomdr.mcp` loaded; bridge listening (Window → Unity-Comdr MCP).  
2. Create a bright **red Cube** in the upper-left of the Game View (or a Canvas Text "SCORE 42" top-left).  
3. Confirm MCP client points at built `UnityComdr.McpHost` and `editor_state.hostMode` is `live`.

## Session steps

1. In Cursor / Codex / Doggy: load skill `screenshots`.  
2. Call `screenshot_capture` with `source=game_view` (optional `maxResolution=640`).  
3. Confirm client shows an **image** (not base64 text).  
4. Ask the model to describe **only from the image**: color, approximate position, any UI text.  
5. Save transcript/log path.

## Pass criteria

- Model correctly reports fixture attributes **without** being told via hierarchy tools in the same turn.  
- Attach path under `docs/ops-loop.md` or audit evidence note.  
- Then mark AC-V6 PASS and close VISION product claim.

## Explicit residual

Until this procedure is done, marketing/docs must say vision **protocol CODE ready / product claim BLOCKED**.
