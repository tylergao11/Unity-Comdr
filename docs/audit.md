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

> **优先级锁：** 精准/准确 **先于** 节省/优化。不得为省 token、省工、过 CI 而交付不准的看见/状态。  
> 产品意图：Codex / Cursor / Doggy 等 agent 经 MCP **真正看见** Unity 运行/UI 画面（对齐 IvanMurzak screenshot 能力）。  
> **不得**用“有截图工具 / 有 base64 字段 / headless marker PASS”宣称已满足。  
> 定案：[`docs/product-ux-frontier.md`](product-ux-frontier.md) §0 / PR-0；需求修正：[`docs/brainstorms/2026-08-03-ux-frontier-addendum.md`](brainstorms/2026-08-03-ux-frontier-addendum.md)。  
> 执行抄点清单（唯一有效）：[`docs/borrow-plan.md`](borrow-plan.md) — 具体抄谁/抄哪个文件/怎么抄 + AI-first 痛点 A1–A10。  
> 产品验收口径（AC-V1…AC-V8，含 vision 预算与 capture 语义）见 [`docs/product-ux-frontier.md`](product-ux-frontier.md) §5 —— 本表仍是债务台账（authoritative ledger），两处须同时满足方可关闭。

| 债务 ID | 状态（Round-1 audit 2026-08-03） | 何谓真正完成 | 证据位置 |
|---------|----------------------------------|--------------|----------|
| `VISION-MCP-IMAGE` | **CODE PASS / CLAIM BLOCKED** — host 已发 `type:image`；全文宣称仍需 AC-V6 实会话 | MCP `type:image` + Cursor/Codex 从图像描述 fixture | `McpServer.ToolsCallResult`; `VisionProtocolTests`; `ToolResult.OkWithImages` |
| `VISION-LIVE-ONLY` | **CODE PASS** — headless `IsRealPixels=false` → `isError`；CI 不能当看见了 | Live 真像素；headless 必须失败 | `InMemoryEditorHost.CaptureScreenshot`; VisionProtocolTests |
| `VISION-SCENE-VIEW` | **CODE PASS (partial)** — 无相机/无 SceneView → Fail；GrabPixels 已接。`isolated` 尚未 Ivan 真隔离 | 真抓取或显式错误；永不 marker 成功；isolated staging 仍 open | `LiveUnityBridgeServer.CaptureScreenshotJson` |

**验收口径（以后任何 agent/审计勾选“能看见 UI”必须同时满足）：**

1. Live Unity 桥开着；  
2. `tools/call` → `screenshot_capture` 返回 **image content**（非仅 text+base64）；  
3. Cursor / Codex 类客户端会话里模型实际吃到图像模态（人工或客户端日志可证）——**Round-1 未完成，CLAIM BLOCKED**；  
4. 不得仅凭 headless / `VisionProtocolTests` fixture 关闭「产品已看见」宣称（协议锁可以，产品宣称不行）。

## Upstream

- NOTICE + THIRD_PARTY: CoplayDev / IvanMurzak / CoderGamester inspiration; original C# implementation.

## Verification snapshot

- `dotnet test` — full suite green (see latest run; includes `SkillSurfaceProductionTests`, `FullLoopWorkflowTests`, `BridgeJsonTests`).
- MCP host double launch: initialize + tools/list (≤15 core) + tools/call.
- Production audit: `docs/production-capability-audit.md` — Editor MCP surface **GO** on shared host path.
