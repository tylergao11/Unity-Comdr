# Unity-Comdr Product & UX Frontier — design probe（设计定案）

**Date:** 2026-08-03  
**Mode:** Design docs only — **no implementation authorized in this pass**. Every backlog item below is doc-complete-only; execution is **BLOCKED until the user unlocks it**.  
**Inputs:** repo docs (requirements 2026-08-02, competitive-audit-full, production-capability-audit, audit.md execution-shortcut debts, launch-readiness, full-flow-status, SECURITY) + OSS frontier survey performed 2026-08-03 (sources cited inline).  
**Authoritative debt registry:** [`docs/audit.md`](audit.md) → `VISION-MCP-IMAGE`, `VISION-LIVE-ONLY`, `VISION-SCENE-VIEW`. This document is the **product acceptance** home for those debts; audit.md remains the ledger.

---

## 1. Current design pillars (as-is)

| # | Pillar | Where it lives | Health |
|---|--------|----------------|--------|
| P1 | **Token-frugal default**: ≤15 core tools, 9 on-demand skills, paginated compact results | requirements FR-TOKEN; `ToolBudget`; competitive audit §5 | Sharp. Still a real differentiator — no competitor defaults this low. |
| P2 | **Zero-runtime local-first**: UPM + pure C# host; no Python/Node/cloud login | spike-transport; SECURITY.md | Sharp. Coplay needs Python/uv, Coder needs Node, Ivan has cloud paths, Unity official needs Unity 6 + AI package. |
| P3 | **Assemble, don't NIH**: absorb Coplay/Ivan/Coder patterns, original C# | requirements Implementation strategy; NOTICE | Healthy. |
| P4 | **Honest audits**: PASS/RESIDUAL/DEBT discipline; no fake-done | production-capability-audit; audit.md | Healthy — and the reason this doc exists: the VISION-* rows are open debts, not closed features. |
| P5 | **Three loops as the product**: code-fix, scene-build, playmode-verify | README Full agent loops; prompts | Aging at the edges — playmode-verify currently ends at a screenshot the agent **cannot actually see** (text+base64). The loop does not close. |

**One-line thesis check:** the "token-frugal, zero-runtime" identity is intact and still wins. The part that has fallen behind the frontier is **what happens after the agent acts** — verification by sight, install friction, and behavior during Editor transitions.

---

## 2. GitHub / OSS frontier signals (surveyed 2026-08-03)

### 2.1 What the leaders shipped

| Project / spec | Frontier signal | Why it matters to us |
|----------------|-----------------|----------------------|
| **CoplayDev/unity-mcp** v9.6.x ([PR #818](https://github.com/CoplayDev/unity-mcp/pull/818), [PR #840](https://github.com/CoplayDev/unity-mcp/pull/840), [manage_camera docs](https://coplaydev.github.io/unity-mcp/reference/tools/core/manage_camera)) | `include_image=true` returns screenshots as **MCP ImageContent blocks** ("AI assistants see screenshots inline"); `max_resolution` downscale cap (default **640 px**); `batch="surround"` = 6-angle **single labeled contact sheet**; `batch="orbit"` = configurable azimuth×elevation grid contact sheet; positioned temp camera (`view_position`/`view_rotation`/`view_target`); `capture_source="scene_view"` with `view_target` framing; explicit doc note that ScreenCapture path **includes Screen Space – Overlay UI** while camera-render path excludes it | Vision moved from "has a screenshot tool" to **vision-priced seeing**: capped resolution, composites instead of N images, framing control, and honest UI-overlay semantics. This is exactly the gap our `VISION-*` debts describe. |
| **CoplayDev CLI** ([CLI examples](https://coplaydev.github.io/unity-mcp/guides/cli-examples)) | Ships a CLI path *"instead of MCP server connection. This avoids reconnection issues that occur when Unity restarts"* | The market leader concedes that **domain reload / Editor restart breaks agent sessions** badly enough to ship a whole parallel transport. Reload resilience is a real, unsolved UX battleground. |
| **IvanMurzak/Unity-MCP** v0.76.x ([tools reference](https://github.com/IvanMurzak/Unity-MCP/wiki/AI-Tools-Reference)) | 71 tools / 46 prompts; **4 screenshot tools**: `screenshot-camera`, `screenshot-game-view`, `screenshot-scene-view`, `screenshot-isolated` (render one GameObject in isolation, optional 2×2 composite) — each "return it as an image for LLM inspection"; environment-aware skill generation (OS, Unity version, installed plugins); per-feature enable/disable in `UserSettings` JSON | Confirms the four capture surfaces users actually want (camera / game view / scene view / isolated object). `screenshot-isolated` is the sleeper: solo devs constantly ask "does *this prefab* look right", not "does the whole frame look right". |
| **CoderGamester/mcp-unity** 1.4.0, Jul 2026 ([release](https://github.com/CoderGamester/mcp-unity/releases/tag/1.4.0)) | Unity Dashboard **MCP App** (`ui://unity-dashboard` resource + `show_unity_dashboard` tool) with play-mode controls and bidirectional agent context; **project-local auto-config for Cursor / Claude Code / Codex CLI with portable relative paths suitable for git-shared config**; bounded `get_gameobject` responses (depth scopes, 5 MB ceiling, **explicit truncation markers** instead of dropping the connection); WebSocket work moved to an update-drained main-thread queue so **requests continue while the Editor is unfocused** | Three lessons: (a) committable project-relative client config is now table stakes; (b) oversized results should degrade with explicit markers, never kill the session; (c) background-Editor responsiveness is a solved problem elsewhere — agents run while the human is in another window. |
| **Unity official MCP** (`com.unity.ai.assistant` 2.x, Unity 6+) ([overview](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.16/manual/integration/unity-mcp-overview.html)) | Relay binary auto-installed at `~/.unity/relay/`; IPC bridge (named pipes / Unix sockets); **pending-connection approval** — first external client must be approved in Project Settings; per-tool enable/disable toggles; multi-instance targeting by **project path or Editor PID**; multi-client simultaneous; `[McpTool]` attribute registration | Unity itself set a **trust-surface bar**: connections are consented, tools are individually toggleable, instances are addressable. It also validates our "no user-installed runtime" bet — but it requires Unity 6 + the AI package, leaving 2021.3–2022.3 LTS solo devs to the OSS field. |
| **MCP spec 2026-07-28** ([tools](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)) + client evidence ([claude-code #72271](https://github.com/anthropics/claude-code/issues/72271)) | `tools/call` results support `{"type":"image","data":<base64>,"mimeType":"image/png"}` content blocks; ACP reuses the same ContentBlock so agent frontends forward without transformation. Claude Code issue states the degraded path plainly: a base64-in-text block means *"the model sees the base64 text but cannot natively parse it"* | **Protocol-level confirmation of `VISION-MCP-IMAGE`:** our current text+base64 result is, by the clients' own account, invisible to vision models. Fixing the content type is not polish; it is the difference between seeing and not seeing. |
| **Client install UX** ([Cursor MCP docs](https://cursor.com/help/customization/mcp), deeplink format) | One-click `cursor://anysphere.cursor-deeplink/mcp/install?name=$NAME&config=$BASE64`; VS Code `vscode://mcp/install?...`; committable `.cursor/mcp.json` project scope merged with global | "Copy JSON from an Editor window" (our current `UnityComdrWindow`) is one generation behind. The frontier is a **button** and a **committed file**. |

### 2.2 Frontier interpretation — where the battleground moved

1. **From "has screenshot" → "vision-priced seeing."** Everyone has a screenshot tool now. The leaders differentiate on: real image content blocks, default resolution caps, N-angles-as-one-composite, framing/isolation control, and honest UI-overlay semantics. Vision now has a *cost model*, exactly like tool schemas do.
2. **From "copy config" → "one click or one commit."** Coplay, Coder, and Unity official all auto-write client configs; Cursor/VS Code accept deeplinks; Coder's config is project-relative and git-shareable.
3. **From "works when connected" → "predictable during transitions."** Domain reload, compile, play-mode enter/exit, and unfocused Editors are where agent sessions die today. Coplay routed around it with a CLI; Coder engineered through it with queues. Nobody has a clean *protocol-level contract* for "Editor is busy, retry in N" — that is open ground.
4. **From "loopback only" → "consent surface."** Unity official ships connection approval and per-tool toggles. Pure loopback binding (our current stance) is necessary but no longer sufficient as trust UX.
5. **Schema-token discipline is now table stakes at the top.** Our ≤15 default is still the best number, but Coplay's skill/CLI docs and Ivan's skills show everyone converging on some on-demand story. The *next* token battleground is **result tokens and vision tokens**, where our pagination + compact results give us a head start we should formalize.

---

## 3. UX gaps ranked by solo + agent value

Ranked by value to one solo developer working with Cursor / Codex / Doggy — not by feature-list coverage.

| Rank | Gap | Evidence | Current Comdr state | User value |
|------|-----|----------|---------------------|------------|
| **G1** | **Agent cannot actually see** (`VISION-MCP-IMAGE` + `VISION-LIVE-ONLY` + `VISION-SCENE-VIEW`) | MCP spec image blocks; Coplay ImageContent; Claude Code "cannot natively parse" text-base64 | `screenshot_capture` returns `type:text` with embedded base64; headless returns synthetic marker | **Highest.** Playmode-verify is our third flagship loop and it currently does not close. Every "does it look right?" question still needs a human's eyes. |
| **G2** | **No vision cost model** | Coplay `max_resolution` default 640; surround/orbit contact sheets = 1 composite image | No downscale policy, no composite concept, no per-call cost note | High. Without a price ceiling, G1 fixed naively would blow the same token budget P1 protects. Seeing must be as budget-disciplined as schemas. |
| **G3** | **First-5-minutes friction** | Cursor/VS Code deeplinks; Coder project-relative committable config; Unity official auto-configure Integrations | Editor window copies JSON snippets for 2 clients; user hand-edits paths | High. SC1 (≤5 min to first call) is a stated success criterion; the current path has more manual steps than all three competitors. |
| **G4** | **No reload/compile resilience contract** | Coplay ships a CLI to dodge reconnects; Coder queues while unfocused | Bridge reconnect behavior undocumented; agent sees timeouts/hangs during domain reload | High. Code-fix loop *causes* recompiles — our flagship loop triggers our least-defined state. Solo devs blame the product, not Unity. |
| **G5** | **Trust surface below official bar** | Unity official pending-connection approval + per-tool toggles | Loopback bind + escape hatches off; no consent moment, no per-tool disable, no invocation log | Medium-high. One agent deleting one wrong asset destroys solo trust permanently. Consent + visibility is cheap insurance. |
| **G6** | **"See UI" ≠ pixels only** | Coplay ScreenCapture-includes-overlay-UI nuance; Ivan `screenshot-isolated`; Coder `unity://gameobject/{id}` serialized detail | `hierarchy_get` is generic; no uGUI/UIToolkit-aware structured read; overlay-UI capture semantics undefined | Medium. For UI bugs the agent needs both the picture *and* the RectTransform/anchor truth. Pixels catch "looks wrong", structure explains *why*. |
| **G7** | **Multi-instance routing** | Unity official targets by project path / PID | Single port 17890, first-come | Medium-low for solos (usually one Editor), but cheap to design now, expensive to retrofit. |

---

## 4. Proposed product principles (v1 — for 定案)

- **PR-1 The loop is the product.** A capability earns a core slot only if it closes or shortens one of the three loops. Feature-checklist parity is a skills concern, never a core concern.
- **PR-2 Seeing has a budget, like schemas do.** Default capture ≤ 640 px longest edge; multi-angle capture returns **one** composite image; every vision-capable tool documents its approximate vision-token price. The 15-tool budget gets a sibling: the **vision budget**.
- **PR-3 Never fake sight（不假装看见）.** A synthetic marker, a file path, or base64-inside-text **never** counts as the agent seeing. When real pixels are impossible (headless, no camera), return an explicit machine-readable failure — honest blindness over fake vision. This principle is the product-level form of the `VISION-*` debts.
- **PR-4 Five minutes to trusted first call.** Install → configured client → first successful verified tool call in ≤ 5 minutes, and every trust escalation (first connection, escape hatches, destructive ops) is a visible, revocable moment — not a buried default.
- **PR-5 Predictable failure beats silent retry.** Domain reload, compile, play transitions, and closed Editors must yield machine-readable states with retry guidance ("editor_reloading, retry ~5s"), never hangs or fake successes. Agents can handle bad news; they cannot handle no news.
- **PR-6 Absorb patterns, not dependencies.** We adopt Coplay's contact sheets, Coder's committable config, official's consent UX — as *patterns* in pure C#. We do not adopt Python, Node, WebView dashboards, or cloud relays to get them.

---

## 5. Acceptance: "agent really sees UI"（真看见 UI 的验收口径）

The four conditions in [`docs/audit.md`](audit.md) §Known execution shortcuts remain the baseline ledger. This section expands them into testable product acceptance. **All AC-V rows must hold before any `VISION-*` debt is closed or any doc/marketing claims "agent can see".**

| ID | Acceptance criterion | Closes / relates |
|----|----------------------|------------------|
| AC-V1 | `tools/call` → `screenshot_capture` returns an MCP content block `{"type":"image","data":<base64 png>,"mimeType":"image/png"}` per MCP spec 2026-07-28 — not text with embedded base64 | `VISION-MCP-IMAGE` |
| AC-V2 | Default capture is downscaled to ≤ 640 px longest edge; caller may raise the cap explicitly; the tool description states the default and its purpose (vision-token cost) | G2 |
| AC-V3 | Game-view capture **includes Screen Space – Overlay UI by default** (ScreenCapture-style path); camera-specific capture documents that overlay canvases are excluded — the difference is stated in the tool result, not just docs | G6, `VISION-SCENE-VIEW` |
| AC-V4 | Scene without any camera: `scene_view`/`game_view` capture returns an explicit machine-readable failure (`isError` or documented error payload naming the cause) — never a marker or silent placeholder success | `VISION-SCENE-VIEW` |
| AC-V5 | Headless host (`InMemoryEditorHost` / bridge down): vision tools return an explicit "no live Editor — cannot capture real pixels" failure; synthetic markers may exist only for non-vision plumbing tests and are never presented as capture success | `VISION-LIVE-ONLY` |
| AC-V6 | **Human-verified vision protocol:** in a real Cursor/Codex session against a live Editor showing a known fixture (e.g. a red cube upper-left, scene name on a UI label), the agent calls `screenshot_capture` and then correctly describes the fixture **from the image alone**. Session evidence (log or transcript) recorded | audit.md condition 3 |
| AC-V7 | Multi-angle capture (when designed) returns **one labeled composite** (contact sheet), not N separate images | G2 |
| AC-V8 | No CI/headless test (`SkillSurfaceProductionTests` or successors) is cited as evidence of vision acceptance; only live-bridge + client-modality evidence counts | audit.md condition 4 |

**关系声明：** audit.md 是债务台账（authoritative ledger）；本节是这些债务的产品验收口径（product acceptance）。两处必须同时满足才能关闭 `VISION-*`。

---

## 6. Experience journeys (design targets)

### J1 — First 5 minutes（首个五分钟）

1. User adds UPM package (git URL or disk). Bridge auto-starts; **Window → Unity-Comdr MCP** shows green.
2. Window offers, per detected client: **(a)** a one-click deeplink button (`cursor://anysphere.cursor-deeplink/mcp/install?...`, `vscode://mcp/install?...`), **(b)** "write project-local config" (e.g. `.cursor/mcp.json` with a project-relative `dotnet exec` path, committable to git), **(c)** copy-JSON fallback (today's behavior).
3. User's first agent message: "check the Unity console." Tool call round-trips; window shows "last client call: console_read, 12s ago".
4. **Moment of trust:** first external connection surfaces a visible consent moment (see DB-6) instead of connecting silently.
5. Designed failure modes: host not built → window says exactly which `dotnet` command to run; port occupied → window suggests `UNITY_COMDR_BRIDGE_PORT`; Editor closed → client gets explicit headless notice, not simulated success.

*Acceptance:* clean machine, ≤ 5 min, zero hand-edited absolute paths, zero Python/Node (SC1 sharpened).

### J2 — Code-fix loop

1. Agent reads errors (`console_read` filtered), reads script, writes fix, triggers compile.
2. **During recompile/domain reload,** any tool call returns `editor_reloading` with retry guidance (PR-5) — the agent waits and retries instead of dying on a socket timeout.
3. Agent re-reads console; errors gone; loop closes without human paste.

*New design content vs today:* step 2 — the resilience contract (DB-5). The loop's happy path already works; the frontier gap is the transition.

### J3 — Scene-build loop

1. Agent builds hierarchy via `scene_manage`/`gameobject_manage`/`component_manage`, reads back with compact `hierarchy_get`.
2. **New closing step:** one capped screenshot (AC-V1/2/3) so the agent confirms visually that "player, ground, light, camera" actually look assembled — catching the classic "everything at origin, camera facing nothing" failure that structure reads cannot catch.

*Acceptance:* SC4 extended — the scene demo ends with the agent's own visual confirmation, not only a hierarchy dump.

### J4 — Playmode-verify with vision（带视觉的运行验证）

1. Agent loads `playmode` skill, enters play, optionally steps N frames.
2. Captures game view **with overlay UI** at capped resolution → receives a real image block (AC-V1..V3).
3. Agent judges against the goal ("score label visible? enemy spawned?"), on failure captures scene view or an isolated object view for diagnosis, stops play, fixes, repeats.
4. `playmode_verify_loop` prompt is upgraded to encode these vision checkpoints explicitly (DB-9), so mid-tier models follow the loop without hand-holding.

*Acceptance:* the demo from AC-V6 run end-to-end in play mode; agent's verdict derived from pixels, evidenced by session transcript.

---

## 7. Design backlog（ordered — DOC-COMPLETE ONLY）

> Every item's deliverable in this phase is a **written spec** (doc-complete). Implementation of any item is **EXECUTION: BLOCKED until the user unlocks it**. Order = product value order from §3.

| # | Item | Doc-complete definition (what the spec must contain) | Execution |
|---|------|------------------------------------------------------|-----------|
| DB-1 | **MCP image content spec** (`VISION-MCP-IMAGE`) | Exact result shape for `screenshot_capture` (image block + optional text metadata block); size/encoding rules; behavior when client caps result size; per-client fallback matrix (Cursor / Claude Code / Codex / others) with degradation order: image block → file path + explicit "client cannot display" note. Never silent text-base64 | **BLOCKED** |
| DB-2 | **Vision budget policy** (G2) | Default ≤ 640 px cap and its override parameter; JPEG-vs-PNG guidance; approximate vision-token cost table per resolution; composite contact-sheet format (grid, labels, single image); which tools are vision-capable and their per-call price note | **BLOCKED** |
| DB-3 | **Capture semantics spec** (`VISION-SCENE-VIEW`, `VISION-LIVE-ONLY`) | Full decision table: {game_view, scene_view, camera, isolated-object} × {live+camera, live no-camera, headless} → exact result (pixels / explicit error payload); overlay-UI inclusion rules; framing (`view_target`) semantics for scene view | **BLOCKED** |
| DB-4 | **First-5-minutes spec** (G3) | Deeplink URL formats + base64 config generation for Cursor and VS Code; project-local committable config templates (`.cursor/mcp.json`, Codex `config.toml`) with relative-path strategy; window UX flow; `doctor`-style self-test definition (what it checks: dotnet present, host built, bridge port, client config found) | **BLOCKED** |
| DB-5 | **Reload-resilience contract** (G4) | Bridge/host state machine (`connected` / `editor_reloading` / `editor_compiling` / `play_transition` / `editor_gone`); the machine-readable busy payload (state + suggested retry ms); host-side behavior (short buffer vs immediate busy); reconnect/backoff rules; how `editor_state` reports transitions; what agents should be told in tool descriptions | **BLOCKED** |
| DB-6 | **Trust surface spec** (G5) | First-connection consent moment (window notification + allow/deny, remembered per client); per-tool/per-skill disable list (UserSettings-style local config); invocation audit log format (local file, no phone-home) and its FR-X3 status upgrade; how escape hatches surface in the window | **BLOCKED** |
| DB-7 | **UI structured-inspection skill design** (G6) | A `ui` skill spec: read uGUI/UIToolkit tree (canvas → RectTransform anchors/pivots/sizeDelta, UIDocument hierarchy) as compact paginated data; pairs with capture for "pixels + truth" UI debugging; explicit non-goal: no UI editing beyond existing component_manage | **BLOCKED** |
| DB-8 | **Multi-instance discovery design** (G7) | Port-per-project convention + discovery manifest (project path, PID, port) location; client-side selection UX (env var or tool argument); collision behavior; explicitly P1, single-Editor default unchanged | **BLOCKED** |
| DB-9 | **Guided-loop prompts v2** (J4) | Rewritten `playmode_verify_loop` and `scene_build_loop` prompt texts embedding vision checkpoints, budget reminders (capped resolution, one composite), and resilience retry etiquette; acceptance = a mid-tier model executes J4 from the prompt alone | **BLOCKED** |

**Not chasing (explicit):** MCP Apps / `ui://` dashboards (WebView-adjacent complexity vs PR-6 — we watch, we don't follow yet); runtime in-game MCP (P2 unchanged); 200-tool default breadth; cloud relay; Unity 6-only features that would raise our 2021.3+ floor.

---

## 8. Requirements linkage

New/clarified requirements locked in the addendum: [`docs/brainstorms/2026-08-03-ux-frontier-addendum.md`](brainstorms/2026-08-03-ux-frontier-addendum.md). The original 2026-08-02 requirements document remains unmodified history.

## 9. Open questions (for the human)

1. **Vision default format:** capped PNG only, or allow JPEG for photographic game views (smaller vision payloads, worse for UI text)? DB-2 needs a default.
2. **Consent strictness (DB-6):** should first-connection approval be **blocking** (Unity-official style: tools refuse until approved) or **notify-only** (window banner, tools work immediately)? Blocking is safer; notify-only is smoother for solo flow.
3. **Client floor for image content (DB-1):** which clients must the fallback matrix treat as first-class — Cursor + Codex + Claude Code confirmed; is "Doggy" a distinct client with its own image-content behavior we should test, or does it ride one of the above?
4. **Resilience buffering (DB-5):** during domain reload, should the host briefly **queue** requests (smoother, riskier) or **immediately return busy-with-retry** (predictable, chattier)? Recommendation: immediate busy — matches PR-5 — but this changes agent-perceived latency.
5. **Does multi-instance (DB-8) stay P1,** or demote to P2 given solo focus? Unity official covers it for Unity 6 users; our marginal value may be low.

---

*This document is design output only. No tool, bridge, host, or package code was changed in producing it.*
