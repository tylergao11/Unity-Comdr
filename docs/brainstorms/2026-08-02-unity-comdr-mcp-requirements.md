# Unity-Comdr MCP — Requirements

**Date:** 2026-08-02  
**Status:** Draft (product decisions locked in brainstorm)  
**Working title:** Unity-Comdr MCP (name TBD)  
**Scope tier:** Deep — product (greenfield)

---

## Problem

Solo Unity developers using AI agents (Cursor, Claude Code, Copilot, etc.) need the agent to **drive the Editor**, not only edit files on disk. Existing open-source Unity MCPs each excel in different dimensions:

| Project | Strength | Gap for our goals |
|---------|----------|-------------------|
| [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) (~13k★) | Mature surface, one-click client config, multi-instance, community default | Python/`uv` dependency; tool model not optimized for token cost |
| [IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP) (~3.8k★) | Skills, reflection, extensibility, profiler, runtime path | Heavier surface by default; cloud/account paths; project path constraints |
| [CoderGamester/mcp-unity](https://github.com/CoderGamester/mcp-unity) (~1.9k★) | Resources, prompts, project-local config, solid docs | Requires Node; Unity 6+ |
| High tool-count forks (e.g. AnkleBreaker) | Breadth checklist | Default token blow-up |

**User pain:** No single free, local-first MCP is simultaneously **complete enough**, **cheap on LLM tokens**, and **trivial to install** for a solo workflow.

**Counterfactual today:** Developers either (1) use Coplay and pay token/setup tax, (2) use IvanMurzak and manage complexity/account surface, (3) paste Console errors by hand, or (4) buy Unity AI when available.

---

## Target user

**Primary:** Solo indie / personal Unity developers  
**Client context:** Cursor, Claude Code, VS Code Copilot, Windsurf, and other MCP hosts  
**Not primary (later):** Multi-person studio pipelines, in-game runtime AI, enterprise SSO/cloud fleets

---

## Outcome

A **local-first Unity MCP** that:

1. Installs as a **single UPM package** (no required Python/Node/cloud account).
2. Keeps **default tool schema token cost low** via core tools + on-demand Skills.
3. Makes two loops best-in-class on day one: **code fix** and **scene build**.
4. Grows completeness by **assembling and refining proven logic** from leading open-source MCPs (not NIH).

---

## Product thesis (one sentence)

**Best-of-breed Unity Editor MCP for solos: UPM-only, token-frugal by default, skill-extensible for full coverage; implement by forking/porting battle-tested code rather than inventing every tool from scratch.**

---

## Cost model (priority order)

> **Amended 2026-08-03:** fidelity outranks thrift. See [`2026-08-03-ux-frontier-addendum.md`](2026-08-03-ux-frontier-addendum.md) Priority correction and [`docs/product-ux-frontier.md`](../product-ux-frontier.md) §0.

1. **Primary — 精准 / 准确 (fidelity)**  
   - Agent-visible Editor state must be truthful (vision = real MCP image + correct pixels; console/hierarchy/busy states honest).  
   - No fake-done, marker-as-sight, or optimization that lies.

2. **Secondary — LLM token / result thrift（在准确之上）**  
   - Small default tool set (~15 core entrypoints).  
   - Domain capability via **Skills** loaded on demand.  
   - Compact tool results (summaries, pagination, path-scoped queries — not full hierarchy dumps by default).  
   - Optional reflect/execute as **escape hatches**, not always-on schema weight.  
   - Vision caps/composites only after sight is real.

3. **Tertiary — user money**  
   - Free to run for local Editor control; no mandatory subscription or cloud account.

4. **Quaternary — install/ops cost**  
   - Prefer zero host runtimes beyond Unity + MCP client.  
   - Accept optional self-hosted relay later; never required for MVP.

---

## Capability model: A + B (locked)

```
┌──────────────────────────────────────────────┐
│  Core tools (~15) — always in context          │
│  console, script, scene, gameobject, assets,   │
│  compile/recompile, basic tests, playmode base │
├──────────────────────────────────────────────┤
│  Skills — on-demand completeness               │
│  animation, ui, terrain, profiling, physics… │
├──────────────────────────────────────────────┤
│  Escape hatches (restricted by default)        │
│  reflect_call, execute_code                    │
└──────────────────────────────────────────────┘
```

**Rationale:** Matches token-first constraint; still markets as “full workflow” via skill catalog; reflection covers long-tail without 200 permanent tools.

**Explicitly not default:** Dumping 70–200 fine-grained tools into every session.

---

## Priority loops

### P0 — Must crush competitors (MVP)

**L1 — Code fix loop**

- Read Unity Console (filter errors/warnings, pagination).  
- Locate and read related scripts/assets.  
- Apply script create/update/delete.  
- Trigger compile / observe compile state.  
- Re-read Console to verify fix.  
- **Success:** Agent closes error → fix → verify without human copy-paste.

**L2 — Scene build loop**

- Scene create/open/save/load.  
- GameObject create/modify/delete/reparent/duplicate/transform.  
- Component add/modify/remove; common property set.  
- Basic material create/assign; prefab create/instantiate basics.  
- Hierarchy/resources as **compact** reads.  
- **Success:** Natural language can stand up a simple playable scene structure end-to-end.

### P1 — After P0 solid

- Play Mode control (play/pause/stop/step) with domain-reload-aware reliability.  
- Screenshots (Game/Scene/camera).  
- Test Runner depth.  
- Profiler skill pack.  
- More domain skills (animation, UI, terrain, navigation…).  
- Multi-instance Unity routing (when multiple Editors open).

### P2 — Later

- Runtime (in-player) MCP for in-game tools / NPC-style hooks.  
- Optional self-hosted/remote relay (still non-mandatory).  
- Community skill distribution.

---

## Non-goals (MVP)

| Non-goal | Why |
|----------|-----|
| Forced cloud account or cloud relay | Contradicts local-first and solo trust |
| Default full tool dump (200+) | Token cost |
| Shipping Runtime MCP in MVP | Scope; architecture may reserve hooks only |
| Replacing Unity official generative AI (sprites, materials, Muse-style) | Different product |
| Being a generic multi-engine MCP platform first | Focus Unity Editor |

**Open for later, not identity-defining:** Unity official MCP coexistence, skill marketplace, Docker cloud host.

---

## Experience requirements

### Install (good UX bar)

1. Add package via UPM git URL (or OpenUPM when published).  
2. Open Editor window: status of bridge (running/stopped).  
3. One-click **Configure detected MCP clients** (Cursor / Claude Code / VS Code / etc.).  
4. First tool call works within ~5 minutes on a clean machine **without** installing Python or Node (hard requirement intent).

### Agent UX

- Tools named and described so mid-tier models can select correctly.  
- Dangerous ops (delete assets, execute arbitrary code, reflect private call) require clear description + default-off or confirmation policy.  
- Results prefer structured short JSON/text; large trees are truncated with “how to dig deeper” hints.  
- Skill list discoverable (`list_skills` / `load_skill`) so agents can expand capability without permanent context bloat.

### Solo defaults

- Works offline for Editor control.  
- Sensible defaults; advanced knobs exist but are not required on day one.

---

## Implementation strategy: assemble, don’t NIH

**Principle:** Prefer **porting, adapting, and integrating** proven logic from leading OSS Unity MCPs over greenfield reinvention. Original work focuses on:

- Token-efficient **capability routing** (core + skills + escape hatch policy).  
- **UPM-only / no host runtime** packaging glue.  
- **Unified safety, result shaping, and skill loader** product layer.  
- Glue and gaps where upstreams diverge.

### Suggested upstream borrowing map (planning will refine licenses)

| Area | Primary reference candidates | Notes |
|------|------------------------------|--------|
| Scene / GO / component ops | CoplayDev, CoderGamester, IvanMurzak | Normalize API to our core tool names |
| Script edit + console loop | CoplayDev, IvanMurzak | Roslyn validation ideas from Coplay |
| Resources / prompts | CoderGamester | Read-only context without tool spam |
| Skills / custom tool attributes | IvanMurzak | Attribute → tool registration patterns |
| Reflection / dynamic execute | IvanMurzak (escape hatch) | Default restricted |
| Client auto-config UX | CoplayDev, CoderGamester | Project vs global config patterns |
| Multi-instance | CoplayDev | P1 |
| Profiler / domain packs | IvanMurzak extensions | P1 skills |
| Transport (stdio / HTTP) | All three | Prefer embeddable C# or vendored binary inside package |

### License hygiene (must-do in planning)

- Track SPDX of each borrowed file/module.  
- Prefer MIT/Apache-compatible paths; attribute in NOTICE/THIRD_PARTY.  
- Do not copy code under incompatible terms without isolation or reimplementation.

### Not “copy whole repo”

- We **curate** modules into one coherent product surface.  
- Public API and skill catalog are **ours**; internals may be adapted upstream code.  
- Divergence is OK where token policy or UPM-only packaging requires it.

---

## Functional requirements (MVP)

### FR-CORE — Bridge

- FR-C1: MCP server reachable from standard clients (stdio and/or local HTTP — exact transport chosen in planning, constrained by no Python/Node host dependency).  
- FR-C2: Unity Editor plugin starts/stops bridge; visible health in Editor UI.  
- FR-C3: Domain reload / play mode transitions do not permanently kill the bridge (documented recovery if brief disconnect).  
- FR-C4: One-click client configuration for at least: Cursor, Claude Code, VS Code Copilot (extend as easy wins).

### FR-TOOLS — Core tools (illustrative set; exact names in planning)

Must cover:

- Console read/clear (filtered).  
- Script read/create/update/delete.  
- Compile / editor state (compiling, play mode flags).  
- Scene create/open/save/get summary.  
- GameObject CRUD + transform + parent.  
- Component add/get/modify (common cases).  
- Asset find / basic material + prefab essentials.  
- Skill: `list_skills`, `load_skill` (or equivalent).  
- Optional gated: `reflect_call`, `execute_code`.

### FR-SKILL — Skill system

- FR-S1: Skills are versioned packages of tools (+ optional prompts/resources).  
- FR-S2: Unloaded skills cost ~zero tool-schema tokens.  
- FR-S3: Loading a skill registers its tools for the session (or until unload).  
- FR-S4: MVP ships ≥2 example skills beyond core (e.g. `testing`, `prefab-advanced` or `materials`) to prove the pipeline.

### FR-TOKEN — Token discipline

- FR-T1: Default connected session exposes only core (+ explicitly loaded skills).  
- FR-T2: Hierarchy/log/asset listings default to limited page size.  
- FR-T3: Document a measurement method for “default tools JSON schema size” and set a budget in planning.

### FR-SEC — Safety

- FR-X1: Destructive and code-execution tools documented; defaults conservative.  
- FR-X2: No phone-home required for MVP tools.  
- FR-X3: Optional audit log of tool invocations (nice-to-have MVP, required P1 if cheap).

---

## Success criteria

| ID | Criterion |
|----|-----------|
| SC1 | Clean machine: UPM install → configure client → successful tool call in ≤ 5 minutes, **without** Python/Node install |
| SC2 | Default tool schema token footprint materially lower than “all tools always on” baselines (measure vs IvanMurzak full set or similar) |
| SC3 | Demo script: introduce a C# nullref → agent fixes via Console loop without human paste |
| SC4 | Demo script: empty scene → agent builds a simple hierarchy (player, ground, light, camera) + saves scene |
| SC5 | Offline Editor control works with no cloud login |
| SC6 | THIRD_PARTY/NOTICE lists major upstream attributions for borrowed logic |

---

## Constraints & assumptions

- **Unity version target (assumption, validate in planning):** aim 2021.3 LTS → 6.x if feasible; if UPM-only MCP host forces a higher floor, document it.  
- **MCP clients** vary in schema quirks; avoid fragile JSON Schema patterns known to break some hosts.  
- **Empty repo today:** `Unity-Comdr` is greenfield; first code is product skeleton + borrowed modules.  
- **User preference:** heavy reference to upstream code is desired; originality is in product architecture and packaging, not every Editor API call.

---

## Open questions (for planning, not blocking product thesis)

1. **Transport:** pure in-process C# MCP vs small **vendored** native/self-contained helper binary inside the package (still no user-installed Python/Node).  
2. **Package identity:** final product name, npm-less branding, OpenUPM id.  
3. **License for *our* code:** MIT vs Apache-2.0 (interaction with upstream mix).  
4. **Exact core tool list and skill split** after inventory of upstream tool catalogs.  
5. **Whether minimal Play Mode appears in late MVP** if code paths come “free” with borrowed modules — default remains P1 unless cheap.

---

## Out of scope for this document

- File layout, class names, concrete protocol frames, CI matrix.  
- Detailed security threat model.  
- Marketing site copy.

Those belong in `/ce-plan` or architecture design.

---

## Handoff notes

**Decisions locked in brainstorm (2026-08-02):**

- Token cost is #1 optimization target.  
- Solo developer is #1 user.  
- P0 = code fix + scene build (parallel).  
- Tool exposure = skills-first core + restricted reflect/execute.  
- Install = UPM-first, minimize host runtimes.  
- No forced cloud.  
- Runtime = P2.  
- **Implementation = assemble from best OSS, not pure original.**

**Suggested next steps:**

1. `/ce-plan` or architecture pass: inventory Coplay / IvanMurzak / CoderGamester modules to port; license matrix; core tool list; skill boundaries.  
2. Spike: UPM package + stdio/HTTP bridge with zero external runtime.  
3. Vertical slice: Console + script edit + one scene tool end-to-end.  
4. Only then expand skill packs.
