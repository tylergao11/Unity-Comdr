# Unity-Comdr Production Capability Audit

**Date:** 2026-08-03  
**Product version:** 0.4.0 (Editor MCP surface)  
**Scope of “全部能力”:** Full **Editor** MCP catalog (core + domain skills + resources/prompts + live bridge properties + install/trust).  
**Out of this product surface (explicit, not silent residual):** In-game Runtime MCP, multi-instance fleet routing, remote cloud relay, OpenUPM publish, 70–200 always-on tools.

Legend: **PASS** = automated evidence on shipped path · **RESIDUAL** = environment-blocked or operator-bound · **FAIL** = none remaining in scope.

---

## 1. Core tools (default session ≤15)

| Capability | Status | Evidence |
|------------|--------|----------|
| `console_read` / filter Error | **PASS** | `P0HandlerTests`, `SkillSurfaceProductionTests`, `FullLoop_CodeFix_*` |
| `console_clear` | **PASS** | batch + skill surface |
| `script_read/write/delete/list` | **PASS** | FullLoop code-fix; script write clears file errors |
| `editor_state` / `editor_compile` | **PASS** | FullLoop; compile clears CS* without console_clear |
| `scene_manage` (get/list/list_opened/create/open/save/unload/set_active) | **PASS** | FullLoop scene-build; parity tests |
| `hierarchy_get` compact + digDeeper | **PASS** | FullLoop; digDeeper names real tool |
| `gameobject_manage` (+ find/tag/layer/primitive) | **PASS** | FullLoop; isolation; mutation post-state |
| `component_manage` (+ list_types) | **PASS** | FullLoop; skill surface |
| `assets_manage` (find/material/prefab/folder/copy/move/refresh/shaders) | **PASS** | FullLoop; skill surface core assets test |
| `skill_manage` list/load/unload | **PASS** | Skill catalog + surface loads all 9 |
| `escape_hatches_set` default off | **PASS** | `Escape_hatches_gated_until_enabled` |
| Default tool count ≤15 | **PASS** | budget tests; MCP tools/list |

---

## 2. Domain skills (on-demand; each non-stub on shared host)

| Skill | Tools | Status | Evidence |
|-------|-------|--------|----------|
| `testing` | tests_run, tests_list, tests_status | **PASS** | `SkillSurfaceProductionTests` + job poll (`AgentUxEnvelopeTests`) |
| `prefab-advanced` | prefab_batch_instantiate, prefab_list | **PASS** | count=3 batch; list contains prefab |
| `playmode` | playmode_control | **PASS** | play/pause/step/stop + editor_state |
| `selection` | selection_manage | **PASS** | set/get returns GO id |
| `packages` | package_manage | **PASS** | list non-empty; search cinemachine; add |
| `menu` | menu_manage | **PASS** | list catalog; execute creates Sphere |
| `profiling` | profiler_manage | **PASS** | start enabled=true; capture/save/load |
| `screenshots` | screenshot_capture | **PASS (honest fail headless)** | headless → isError; live → MCP type:image; `isolated` = Ivan layer+staging cam (see §5) |
| `batch` | batch_execute | **PASS** | multi call content; dryRun |

---

## 3. Resources & prompts

| Item | Status | Evidence |
|------|--------|----------|
| `resources/list` + `unity://*` read | **PASS** | `Mcp_resources_and_prompts_protocol` |
| `prompts/list` + `code_fix_loop` get | **PASS** | same |
| `unity://skills` meta | **PASS** | ResourceCatalog + DomainSkills.CatalogMeta |

---

## 4. Full agent loops + Phase E envelope

| Loop / surface | Status | Evidence |
|----------------|--------|----------|
| Code-fix (no vacuous console_clear) | **PASS** | `FullLoop_CodeFix_*`, `CodeFix_recompile_clears_cs_errors_without_console_clear` |
| Scene-build + isolation | **PASS** | `FullLoop_SceneBuild_*` |
| Playmode-verify + screenshot | **PASS** | `FullLoop_PlaymodeVerify_*` |
| Ok/Error envelope + nextStep | **PASS** | `AgentUxEnvelopeTests` |
| Mutation post-state echo | **PASS** | same (GO/script/component) |
| dryRun preview | **PASS** | delete/batch dryRun |

---

## 5. Live bridge production properties

| Property | Status | Evidence |
|----------|--------|----------|
| `BridgeClientEditorHost` implements `IEditorHost` | **PASS** | structural + factory |
| `LiveUnityBridgeServer` auto-start TCP loopback | **PASS** | package source + InitializeOnLoad |
| Host prefers live / headless fallback | **PASS** | `EditorHostFactory` tests |
| Hierarchy rootObjectIds / childIds filled | **PASS** | SerializeScene/SerializeGo + BridgeJson hierarchy test |
| findMany name/tag/componentType filters | **PASS** | live source + headless FindGameObjects |
| script `\n` + `\uXXXX` unescape (no i+=4) | **PASS** | BridgeJsonTests + live source parity |
| console get/clear/add path | **PASS** | live log callback + headless |
| selection get/set real Selection API | **PASS** | live parses `gameObjectIds`/`assetPaths` **arrays** (BridgeClient wire) + goIds CSV; SerializeSelection |
| package list/add/remove/search | **PASS** | live reads/writes `Packages/manifest.json` + PackageCache (no main-thread UPM hang); headless catalog tests |
| menu list catalog + ExecuteMenuItem | **PASS** | live + headless tests |
| profiler real memory counters when possible | **PASS** | live Profiler.*; headless sampling metrics |
| assets.find via AssetDatabase | **PASS** | live FindAssetsJson |
| component modify applies properties | **PASS** | live SerializedObject apply (not fake true) |
| screenshot camera / ScreenCapture / scene_view on live bridge | **PASS** (code) | Coplay-ported CaptureComposited + camera.Render + GrabPixels; region crop native; whole-frame ≤640 |
| screenshot `source=isolated` | **PASS** (code) | Ivan-style temp layer 31 + staging camera/light; layers/activeSelf restored in `finally`; limitations in JSON `note` (no Composite/custom lights; OnEnable side effects not rewindable) |
| agent vision: MCP `type:image` | **CODE PASS / CLAIM BLOCKED** | Host emits `type:image` (`VisionProtocolTests`). Product claim still needs **AC-V6** live client transcript. Tracked: `docs/audit.md` + `docs/ops-loop.md` |
| reload busy + compileEpoch / sessionGeneration | **PASS** (code) | Phase R + O1/O2; **SC10** Live kill-test still operator residual |
| Live Editor E2E in this sandbox | **RESIDUAL** | No controllable Unity instance required for gate; operator must open Editor for PNG/consent/reload E2E. Documented — not claimed PASS. |

---

## 6. Install (Phase I)

| Item | Status | Evidence |
|------|--------|----------|
| FR-I1 Cursor/VS Code mcp/install deeplink | **PASS** (code) | `ClientConfigBuilder` + `UnityComdrWindow`; `ClientConfigBuilderTests` |
| FR-I2 project-local relative host path config | **PASS** (code) | `.cursor/mcp.json` / `.vscode/mcp.json` / `.claude/mcp.json` / `.codex/config.toml` |
| FR-I3 doctor (bridge/port/last call/host DLL) | **PASS** (code) | `DoctorChecks` / `DoctorReport` + Window |
| Operator one-click Install in Editor | **RESIDUAL** | Needs human click in Window → Unity-Comdr MCP |

---

## 7. Trust (Phase T)

| Item | Status | Evidence |
|------|--------|----------|
| FR-T1 first-connection consent (blocking) | **PASS** (code) | `BridgeTrust.EnsureConsent`; Core `ConsentState` in `TrustPolicyTests` |
| FR-T2 per-tool / per-skill disable | **PASS** (code) | `TrustSettings` + live method disable + Window |
| FR-T3 local invocation audit log | **PASS** (code) | `AppendAudit` / `MemoryAuditSink` |
| Operator consent click | **RESIDUAL** | First live tool method must be Approved once (or pre-approved in Window) |
| No forced cloud/login | **PASS** | host start path; SECURITY.md |
| Escape hatches default off | **PASS** | gated test |
| Bridge binds loopback only | **PASS** | `IPAddress.Loopback` |
| Token-frugal default session | **PASS** | budget ≤15 |

---

## 8. Ship gate

| Gate | Status | How to verify |
|------|--------|----------------|
| `dotnet test` | **PASS** | `dotnet test UnityComdr.sln -c Release` |
| MCP host double launch | **PASS** | `McpHostProcessTests` + launch logs |
| Go-live operator path | **PASS** | README Go-live + this audit |
| Residual honesty | **PASS** | AC-V6 transcript + SC10 kill-test + operator consent click listed open; Runtime non-goal |

---

## 9. Decision

| Audience | Verdict |
|----------|---------|
| **Editor MCP production grade (shared host + skill surface)** | **GO** (0.4.0) |
| **Live Editor on operator machine** | **CONDITIONAL** — code path production-ready; residuals: AC-V6 transcript, SC10 kill-test, consent click |
| **Runtime in-game MCP** | **Out of surface** (separate product; not Editor MCP “全部能力”) |

---

## 10. Test index (shipped CallAsync)

| Suite | Role |
|-------|------|
| `SkillSurfaceProductionTests` | Every skill representative success |
| `FullLoopWorkflowTests` | Three full loops + live structural |
| `BridgeJsonTests` | Unescape + live parity locks |
| `SkillAndToolCatalogTests` | Budget + escape + load/unload |
| `P0HandlerTests` / `ParityAndDomainSkillTests` | Core depth |
| `McpHostProcessTests` / `McpProtocolTests` | Host protocol |
| `VisionProtocolTests` | MCP `type:image` + honest headless fail |
| `AgentUxEnvelopeTests` | Phase E envelopes / mutation / dryRun / jobs |
| `ClientConfigBuilderTests` | Phase I deeplink / relative config / doctor |
| `TrustPolicyTests` | Phase T consent / disable / audit |
| `ReloadResilienceTests` / `AccuracyEpochTests` | Phase R + O1/O2 |

```bash
dotnet test UnityComdr.sln -c Release
```
