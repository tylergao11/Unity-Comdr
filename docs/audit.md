# Code audit notes (2026-08-02, updated)

> **Production capability audit (authoritative PASS/FAIL/RESIDUAL):**  
> [`docs/production-capability-audit.md`](production-capability-audit.md)  
> **Launch go/no-go:** [`docs/launch-readiness.md`](launch-readiness.md)  
> **Competitive matrix:** [`docs/competitive-audit-full.md`](competitive-audit-full.md)

## Structure

| Layer | Path | Role |
|-------|------|------|
| UPM | `packages/com.unitycomdr.mcp` | LiveUnityBridgeServer, Editor window, package.json |
| Core | `src/UnityComdr.Core` | IEditorHost, InMemory + BridgeClient, tools, skills, BridgeJson |
| Host | `src/UnityComdr.McpHost` | MCP stdio entry; prefers live bridge, headless fallback |
| Tests | `tests/UnityComdr.Tests` | 34+ tests: full loops, all skills, BridgeJson, MCP process |

## Security / privacy

- **No phone-home** telemetry in host startup.
- **No secrets** embedded.
- **Escape hatches** default **off** (`escape_hatches_set`).
- **Cloud** not required for local Editor control.
- Live bridge binds **127.0.0.1** only.

## Residual risks (honest — match production audit)

1. **Live Editor E2E in CI/sandbox** — environment-blocked without a controllable Unity process. Live **code path is implemented** (Selection arrays, manifest packages, AssetDatabase find, Profiler counters, full PNG + filePath, SerializedObject modify). Operator must open Unity + package once for confidence. **Not** “live host is still a stub.”
2. **Runtime in-game MCP** — separate product surface (Non-goal of Editor MCP).
3. **MCP framing** — newline-delimited JSON-RPC (stdio). Clients needing Content-Length may need an adapter.
4. **`execute_code` / live Roslyn** — gated escape hatch; headless dry-run by design.
5. **Multi-instance fleet routing** — not productized.
6. **Agent vision / “真看见 UI” 偷懒债（产品硬要求，禁止当 DONE）** — 见下节。

## Known execution shortcuts（禁止当对齐完成）

> 产品意图：Codex / Cursor / Doggy 等 agent 经 MCP **真正看见** Unity 运行/UI 画面（对齐 IvanMurzak screenshot 能力）。  
> **不得**用“有截图工具 / 有 base64 字段 / headless marker PASS”宣称已满足。  
> 产品验收口径（AC-V1…AC-V8，含 vision 预算与 capture 语义）见 [`docs/product-ux-frontier.md`](product-ux-frontier.md) §5 —— 本表仍是债务台账（authoritative ledger），两处须同时满足方可关闭。

| 债务 ID | 现状（偷懒点） | 何谓真正完成 | 证据位置 |
|---------|----------------|--------------|----------|
| `VISION-MCP-IMAGE` | `screenshot_capture` 经 `McpServer.ToolsCallResult` 只回 `content[].type = "text"`，把 `pngBase64` 塞进 JSON 字符串 | MCP 工具结果含标准 **`type: "image"`**（或客户端约定的 image content），vision agent 可直接当图看，而不是自己解析巨型 text | `src/UnityComdr.McpHost/McpServer.cs`（text-only）；`LiveUnityBridgeServer.CaptureScreenshotJson`（有 PNG 但未升格为 image content） |
| `VISION-LIVE-ONLY` | headless `InMemoryEditorHost.CaptureScreenshot` 只返回 synthetic `payloadMarker` | Live Editor + bridge 下真实像素；CI 不得用 marker 冒充“看见了” | `InMemoryEditorHost.cs`；`production-capability-audit.md` screenshots 行 |
| `VISION-SCENE-VIEW` | 无 Camera 时 `scene_view` / `game_view` 退回 marker note | Scene/Game 视图在无 Main Camera 时仍有可用画面路径（或明确失败，不假成功） | `LiveUnityBridgeServer.CaptureScreenshotJson` |

**验收口径（以后任何 agent/审计勾选“能看见 UI”必须同时满足）：**

1. Live Unity 桥开着；  
2. `tools/call` → `screenshot_capture` 返回 **image content**（非仅 text+base64）；  
3. Cursor / Codex 类客户端会话里模型实际吃到图像模态（人工或客户端日志可证）；  
4. 不得仅凭 `SkillSurfaceProductionTests` / headless marker 关闭本债务。

## Upstream

- NOTICE + THIRD_PARTY: CoplayDev / IvanMurzak / CoderGamester inspiration; original C# implementation.

## Verification snapshot

- `dotnet test` — full suite green (see latest run; includes `SkillSurfaceProductionTests`, `FullLoopWorkflowTests`, `BridgeJsonTests`).
- MCP host double launch: initialize + tools/list (≤15 core) + tools/call.
- Production audit: `docs/production-capability-audit.md` — Editor MCP surface **GO** on shared host path.
