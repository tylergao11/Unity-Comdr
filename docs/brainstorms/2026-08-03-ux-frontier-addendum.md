# Unity-Comdr MCP — Requirements Addendum: UX Frontier

**Date:** 2026-08-03  
**Status:** Locked (design 定案) — extends, does not rewrite, [`2026-08-02-unity-comdr-mcp-requirements.md`](2026-08-02-unity-comdr-mcp-requirements.md)  
**Basis:** OSS frontier probe of 2026-08-03 — see [`docs/product-ux-frontier.md`](../product-ux-frontier.md) for evidence and acceptance detail.  
**Scope note:** These are requirement lockdowns only. **No implementation is authorized by this document**; all execution remains blocked until the user unlocks it.

---

## Why an addendum

The 2026-08-02 requirements predate three frontier shifts confirmed on 2026-08-03:

1. Leading Unity MCPs now return screenshots as **real MCP image content** with resolution caps and composite contact sheets (CoplayDev v9.6.x; IvanMurzak v0.76.x) — "agent vision" is table stakes at the top, and text+base64 is confirmed invisible to client vision pipelines.
2. Client config moved to **one-click deeplinks and committable project-local files** (Cursor/VS Code deeplinks; CoderGamester 1.4.0 project-relative auto-config; Unity official auto-configure).
3. Unity's **official MCP** (Unity 6 + `com.unity.ai.assistant`) set a trust bar: connection approval, per-tool toggles, PID/project-path instance targeting.

The original document's thesis (token-frugal, UPM-only, skills-first, solo-first) survives unchanged. The additions below sharpen *verification by sight*, *install friction*, *transition resilience*, and *trust*.

---

## New / clarified functional requirements

### FR-VIS — Agent vision (upgrades P1 "Screenshots" from feature to acceptance-gated capability)

- **FR-V1:** Screenshot tool results MUST use MCP `type:"image"` content blocks (base64 + mimeType) per current MCP spec — never base64 embedded in a text block. (Debt `VISION-MCP-IMAGE`.)
- **FR-V2:** Default captures are resolution-capped (≤ 640 px longest edge, overridable) and multi-angle captures return a single labeled composite. Vision cost is a documented budget, sibling to the ≤15-tool schema budget.
- **FR-V3:** Game-view capture includes Screen Space – Overlay UI by default; camera-specific capture documents its overlay exclusion. No-camera and headless situations return explicit machine-readable failures — synthetic markers never masquerade as sight. (Debts `VISION-SCENE-VIEW`, `VISION-LIVE-ONLY`.)
- **FR-V4:** "Agent can see UI" may only be claimed after the full AC-V1…AC-V8 acceptance table in [`docs/product-ux-frontier.md`](../product-ux-frontier.md) §5 passes, including a human-verified live client session.

### FR-INS — Install (sharpens original "Experience requirements → Install")

- **FR-I1:** The Editor window offers per-client one-click configuration via deeplink (`cursor://…/mcp/install`, `vscode://mcp/install`) where the client supports it.
- **FR-I2:** The window can write **project-local, committable** client config (e.g. `.cursor/mcp.json`, Codex `config.toml` fragment) using project-relative paths — no hand-edited absolute paths on the happy path.
- **FR-I3:** A self-test ("doctor") surface reports: dotnet available, host built, bridge port status, client config detected, last successful client call.

### FR-REL — Transition resilience (new; was implicit in FR-C3)

- **FR-R1:** Bridge/host expose a machine-readable Editor state machine (`connected` / `editor_reloading` / `editor_compiling` / `play_transition` / `editor_gone`). During transitions, tool calls return an explicit busy payload with suggested retry delay — never hangs, never fake success.
- **FR-R2:** Guided prompts teach agents the retry etiquette so the code-fix loop (which *causes* recompiles) survives its own side effects.

### FR-TRUST — Consent surface (upgrades FR-SEC)

- **FR-T1:** First connection from an external MCP client is a visible consent moment in the Editor (blocking or notify-only — open question #2 in the frontier doc).
- **FR-T2:** Per-tool / per-skill local disable list (UserSettings-style; not cloud, not phone-home).
- **FR-T3:** Local invocation audit log upgraded from "nice-to-have" (original FR-X3) to **required at P1**.

---

## New success criteria (extend SC1–SC6)

| ID | Criterion |
|----|-----------|
| SC7 | Live Editor + real client session: agent captures a fixture scene and correctly describes it **from the returned image alone** (AC-V6 protocol, transcript evidence). |
| SC8 | Playmode-verify demo closes end-to-end on vision: play → capped capture with overlay UI → agent verdict from pixels → stop/fix — no human screenshot pasting. |
| SC9 | Clean machine first-call ≤ 5 min using only deeplink or written project-local config (no hand-edited absolute paths). |
| SC10 | Kill-test: trigger a domain reload mid-session; agent receives explicit busy states and completes the loop after retry — no hung sockets, no fake results. |

---

## Priority note

- The three P0 loops are unchanged, but **playmode-verify's definition of done now includes vision** (SC8). A playmode loop whose screenshot the agent cannot see is not "done" at any tier.
- Multi-instance routing remains P1 pending open question #5 (possible demotion to P2 given Unity official coverage on Unity 6).
- Runtime in-game MCP remains P2. MCP Apps / `ui://` dashboards are explicitly not chased (frontier doc §7 "Not chasing").

---

## Traceability

| This addendum | Source |
|---------------|--------|
| FR-VIS | `docs/audit.md` VISION-* debts (authoritative ledger) + frontier doc §5 acceptance |
| FR-INS | Frontier doc §2.1 (Cursor deeplink, CoderGamester 1.4.0, Unity official Integrations) |
| FR-REL | Frontier doc §2.2 point 3 (Coplay CLI workaround, CoderGamester queue) |
| FR-TRUST | Frontier doc §2.1 (Unity official approval + toggles) |

*Design lockdown only. No code changed.*
