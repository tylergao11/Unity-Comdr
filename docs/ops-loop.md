# Unity-Comdr 运营循环：设计 → 执行 → 审计 → 设计

**状态：** ACTIVE（2026-08-03 用户解锁）  
**目标：** 上线级别品质（Editor MCP production + Live vision AC-V1–V10）  
**权威计划：** [`borrow-plan.md`](borrow-plan.md) · [`product-ux-frontier.md`](product-ux-frontier.md) · [`audit.md`](audit.md)

---

## 1. 循环规则（硬）

| 角色 | 谁 | 允许 | 禁止 |
|------|----|------|------|
| **设计** | 主代理 / Fable | 改 docs、验收口径、抄点表 | 不写实现冒充定案 |
| **执行** | Writer（优先 Grok；可委派） | 按 borrow-plan 改代码；拿不准就读上游源码 | **禁止自创协议捷径**；禁止 text+base64 当看见；禁止 marker 当 PASS；禁止自证完成 |
| **测试** | 独立 Test pass | `dotnet test` + 针对本阶段的断言；Live 有条件则跑 | 禁止用 headless marker 关闭 VISION 债 |
| **审计** | 独立 Audit pass（主代理或 Bugbot） | 对照 AC-V / FR / borrow-plan 抓偷懒 | 禁止信任 Writer 的“已完成”话术 |

**两失败规则：** 连续两次修不好 → 停手做根因分析，禁止继续缝补。  
**完整交付：** stub / 90% / “代码路径已就绪”不算上线；见 delivery-complete-gate。

---

## 2. 反偷懒检查清单（每次审计必勾）

执行端历史上已犯过的偷懒，审计时逐项搜：

| ID | 偷懒形态 | 审计搜什么 | 否决条件 |
|----|----------|------------|----------|
| L1 | MCP 回 `type:text` 塞 base64 | `McpServer.ToolsCallResult` 是否含 `type":"image"` | 无 image block → **FAIL** |
| L2 | headless marker 当视觉成功 | `CaptureScreenshot` / tests 是否断言 `payloadMarker` 即 success | marker 冒充 sight → **FAIL** |
| L3 | 无相机仍返回假成功 | game_view/scene_view 无相机路径 | 非 `isError` → **FAIL** |
| L4 | 整屏 640 砍掉细节 QA | region crop 是否被强制 downscale | crop 被砍 → **FAIL** |
| L5 | 写 UI 只回 true | mutation 是否有视觉/状态回读（O6 阶段） | Phase E/V 要求未满足 → **FAIL** |
| L6 | 未读上游就自创 | 本阶段 diff 是否偏离 borrow-plan 抄点 | 无 THIRD_PARTY 行且算法自创 → **FAIL** |
| L7 | 自证完成 | 是否有独立 test + audit 证据 | 仅 Writer 声称 → **FAIL** |

---

## 3. 当前冲刺

| 字段 | 值 |
|------|-----|
| Sprint | **Phase V — 看见** |
| 范围 | AC-V1–V10 中可无 Live Editor 锁定的部分先硬编码进协议与测试；Live 路径对源码移植 Coplay Cameras + Ivan Overlay |
| Writer | Grok 4.5 High Fast（Task） |
| Tester | 独立 pass：`dotnet test` + 新增 vision protocol 测试 |
| Auditor | 主代理对照本节 L1–L7 + AC-V |
| 上游必读 | Coplay `MCPForUnity/Editor/Tools/Cameras/*`；Ivan screenshot wiki/源；MCP spec image content |

---

## 4. 循环日志

| 轮次 | 阶段 | 结果 | 证据 |
|------|------|------|------|
| 0 | 设计 | borrow-plan + frontier + VISION 债 + G8 | docs/* |
| 1 | 执行 Phase V | Writer (Grok) 交付 | MCP image + live ScreenCapture port + honest headless |
| 1 | 测试 | **PASS** | `dotnet test -c Release` → 38 passed / 0 failed（独立主代理重跑） |
| 1 | 审计 | **CONDITIONAL PASS** — 见 §4.1 | L1–L4/L6 代码级通过；AC-V6 Live 人工会话 + isolated 真隔离仍 RESIDUAL |

### 4.1 Round-1 Audit（主代理 · 对照 L1–L7）

| Check | Verdict | Evidence |
|-------|---------|----------|
| L1 text+base64 only | **PASS** | `McpServer.ToolsCallResult` emits `type:image`; `VisionProtocolTests` asserts image block + text must not contain PNG b64 |
| L2 headless marker success | **PASS** | `InMemoryEditorHost` `IsRealPixels=false`; headless `screenshot_capture` → `isError=true` |
| L3 no-camera fake success | **PASS** (code) | Live `CaptureScreenshotJson` throws → `Fail` → BridgeClient throws → skill `Error`; no marker success JSON |
| L4 region crop downscaled | **PASS** (code) | `hasRegion` branch skips `DownscaleTexture` |
| L5 UI mutation visual回读 | **N/A** | Phase E/O6 — not this sprint |
| L6 invented without upstream | **PASS** | `THIRD_PARTY.md` Phase V table; Downscale/CaptureComposited/WaitForEndOfFrame from Coplay refs |
| L7 writer self-certify | **PASS process** | Independent test re-run by auditor; Writer explicitly disclaimed AC-V |

**Residuals (do not close full VISION ledger yet):**

1. **AC-V6** — no human Cursor/Codex live-Editor transcript yet (env).
2. **`isolated` source** — was camera.Render partial; **upgraded in residual polish** to Ivan temp layer + staging camera (see Round-6 launch residuals).
3. **AC-V3 Overlay UI** — ScreenCapture path present; Live play-mode pixel proof not run here.
4. Stale marketing rows in older audits were outdated; updated in this round.

**Next design→execute:** Phase **R** (busy/reload contract) while AC-V6 remains operator residual on Phase V.

| 轮次 | 阶段 | 结果 | 证据 |
|------|------|------|------|
| 2 | 执行 Phase R | Writer (Grok) 交付 | EditorLifecycle + busy gate + live hooks |
| 2 | 测试 | **PASS** | `dotnet test -c Release` → **48** passed / 0 failed（独立主代理重跑） |
| 2 | 审计 | **CONDITIONAL PASS** — 见 §4.2 | FR-R1/R2 代码级；O1 epoch / O2 generation / Live kill-test SC10 仍 RESIDUAL |

### 4.2 Round-2 Audit（Phase R）

| Check | Verdict | Notes |
|-------|---------|-------|
| Immediate busy, no silent queue | **PASS** | `TryImmediateBusyResponse` + `EditorBusyException`; ToolRegistry gates non-`editor_state` tools |
| `editor_state` still readable when busy | **PASS** | ReloadResilienceTests |
| Actionable error (phase + retry + nextStep) | **PASS** | FormatBusyMessage |
| Prompt retry etiquette FR-R2 | **PASS** | `code_fix_loop` contains phases |
| Live hooks compile/reload/play | **PASS** (source) | beforeAssemblyReload / playModeStateChanged / isCompiling |
| O1 compile epoch / O2 ID generation | **RESIDUAL → see Round-3** | Deferred in Round-2; executed Round-3 |
| SC10 Live kill-test | **RESIDUAL** | Needs operator Unity domain-reload mid-session |

| 轮次 | 阶段 | 结果 | 证据 |
|------|------|------|------|
| 3 | 执行 O1/O2 | Writer (Grok) 交付 | compileEpoch + sessionGeneration + stale_reference |
| 3 | 测试 | **PASS** | 全量 **54** passed；AccuracyEpochTests 6/6（独立重跑） |
| 3 | 审计 | **PASS (code)** | O1/O2 覆盖 A6/A7；Live afterAssemblyReload 仍待 SC10 |

### 4.3 Round-3 Audit（O1/O2）

| Check | Verdict |
|-------|---------|
| compile bumps compileEpoch | **PASS** |
| console entries carry epoch + stale after compile | **PASS** |
| sessionGeneration bump remints IDs; old id → stale_reference | **PASS** |
| path-based re-find after generation bump | **PASS** |
| Live afterAssemblyReload bumps generation | **PASS** (source) / Live E2E residual |

**Next:** Phase **E**（envelope / mutation 回读 / 可行动错误 A2–A4）→ 然后 I/T；AC-V6 仍等 Live 操作者会话。

| 轮次 | 阶段 | 结果 | 证据 |
|------|------|------|------|
| 4 | 执行 Phase E | Writer 交付 | Ok/Error envelope · mutation 回读 · dryRun · tests job · digDeeper |
| 4 | 测试 | **PASS** | 独立主代理重跑：E 后全量绿（其后 I/T 叠至 **80**） |
| 4 | 审计 | **PASS (code)** — 见 §4.4 | O3–O5 / A4–A5 / A9 代码级；视觉 UI 回读仍属 Live residual |

### 4.4 Round-4 Audit（Phase E）

| Check | Verdict | Notes |
|-------|---------|-------|
| Ok/Error envelope 稳定形状 | **PASS** | `ToolResult.OkEnvelope` / `ErrorEnvelope`；`ok`/`data`/`error.code`/`nextStep` |
| Registry 成功/失败均 envelope | **PASS** | `AgentUxEnvelopeTests.Registry_wraps_*` |
| Mutation 回读（post-state） | **PASS** | GO create/rename/transform、script_write、component add 含摘要字段 |
| dryRun 不改世界 | **PASS** | delete/batch dryRun |
| 可行动错误 nextStep | **PASS** | skill 未加载等路径含 nextStep |
| 长操作 job 化（tests） | **PASS** (code) | `tests_run` → job id；`tests_status` poll |
| Live UI 视觉回读（O6） | **RESIDUAL** | 需 Live Editor + AC-V6 会话 |

| 轮次 | 阶段 | 结果 | 证据 |
|------|------|------|------|
| 5 | 执行 Phase I | Writer 交付 | Deeplink · project-local mcp.json/toml · Doctor · Window UI |
| 5 | 测试 | **PASS** (code) | `ClientConfigBuilderTests`（相对路径、Cursor/VS Code deeplink、Codex TOML、DoctorReport） |
| 5 | 审计 | **CONDITIONAL PASS** — 见 §4.5 | FR-I1/I2/I3 代码级；操作者点一次 Install/Doctor 仍 RESIDUAL |

### 4.5 Round-5 Audit（Phase I）

| Check | Verdict | Notes |
|-------|---------|-------|
| FR-I1 deeplink | **PASS** (code) | `BuildCursorDeeplink` / `BuildVsCodeDeeplink` + Window 按钮 |
| FR-I2 project-local 相对路径 | **PASS** (code) | `.cursor/mcp.json` / `.vscode/mcp.json` / `.claude/mcp.json` / `.codex/config.toml` |
| FR-I3 doctor | **PASS** (code) | bridge port / last call / host DLL / FORCE_HEADLESS note |
| Operator 一次点击 Install | **RESIDUAL** | 需真人在 Unity Window → Unity-Comdr MCP 点 deeplink/write config |

| 轮次 | 阶段 | 结果 | 证据 |
|------|------|------|------|
| 6 | 执行 Phase T | Writer 交付 | BridgeTrust consent · per-method disable · audit log · Core TrustPolicy |
| 6 | 测试 | **PASS** (code) | `TrustPolicyTests`（consent / disable / audit / settings roundtrip） |
| 6 | 审计 | **CONDITIONAL PASS** — 见 §4.6 | FR-T1–T3 代码级；操作者首次 consent 点击仍 RESIDUAL |

### 4.6 Round-6 Audit（Phase T）

| Check | Verdict | Notes |
|-------|---------|-------|
| FR-T1 first-connection consent | **PASS** (code) | `BridgeTrust.EnsureConsent` blocking dialog；doctor 豁免 |
| FR-T2 per-tool/skill disable | **PASS** (code) | Core `TrustSettings` + live `IsBridgeMethodDisabled` + Window |
| FR-T3 local audit log | **PASS** (code) | `AppendAudit` + MemoryAuditSink tests |
| Operator consent click | **RESIDUAL** | 需真人首次连桥时 Approve（或 Window 预批） |

**Launch residuals (仍不关闭全账本):**

1. **AC-V6** — Live Cursor/Codex 会话从 image 描述 fixture 的人工 transcript。
2. **SC10** — Live domain-reload mid-session kill-test。
3. **Operator consent click** — 首次桥接审批一次真人点击。
4. **`isolated`** — 已升为 Ivan-style temp layer + staging camera（见 residual polish）；Composite/custom lights 未移植；OnEnable 副作用 limitation 写在 note 字段。

### 4.7 Final gate（主代理 2026-08-03）

| Item | Verdict |
|------|---------|
| Independent `dotnet test -c Release` | **80 passed / 0 failed** |
| Package version | **0.4.0** |
| Borrow-plan phases V→R→O1/O2→E→I→T + isolated polish | **CODE COMPLETE** |
| Product claim “agent really sees UI” | **BLOCKED** until AC-V6 |
| Launch decision | **GO** for local open-source Editor MCP with residuals in `launch-readiness.md` |
