# Unity-Comdr Launch-Readiness Checklist

**Superseded detail for capability rows:** see **[`docs/production-capability-audit.md`](production-capability-audit.md)** (production-grade PASS/FAIL/RESIDUAL).

**Date:** 2026-08-03  
**Package:** `com.unitycomdr.mcp` **0.4.0**  
**Decision:** **GO** for Editor MCP production surface on shared host path — with explicit operator residuals below (not silent).

## Quick operator go-live

1. UPM: `packages/com.unitycomdr.mcp`  
2. `dotnet build UnityComdr.sln -c Release`  
3. `dotnet test UnityComdr.sln -c Release`  
4. MCP client → `dotnet exec …/UnityComdr.McpHost.dll`  
5. Optional live: open Unity (bridge `127.0.0.1:17890`); approve **Bridge consent** on first tool call (or Window → Unity-Comdr MCP → Approve)  
6. Optional CI: `UNITY_COMDR_FORCE_HEADLESS=1`  

## Summary gate

| Axis | Status |
|------|--------|
| Install / README / deeplink + project-local config | **PASS** (code) — operator one-click Install residual |
| Core + 9 skills non-stub (CallAsync) | **PASS** — production audit §1–2 |
| Three full loops | **PASS** |
| Envelope / mutation echo / dryRun (Phase E) | **PASS** (code) |
| Trust consent + disable + audit (Phase T) | **PASS** (code) — operator consent click residual |
| Live bridge properties + parity tests | **PASS** |
| Vision MCP `type:image` + honest headless | **PASS** (code) |
| Isolated screenshot (Ivan layer+staging cam) | **PASS** (code) — Composite/lights JSON not ported |
| Live Editor E2E in CI sandbox | **RESIDUAL** (env) |
| Security defaults (loopback + escape off) | **PASS** |
| Runtime MCP | Out of Editor product surface |

## Explicit launch residuals (GO with these open)

| Residual | Why open | Close when |
|----------|----------|------------|
| **AC-V6 live transcript** | Needs human Cursor/Codex session vs live Editor fixture; agent describes scene **from image alone** | Session log/transcript attached to audit |
| **SC10 live kill-test** | Needs operator Unity domain-reload mid-session; prove busy + retry, no hung socket | Kill-test notes in ops-loop |
| **Operator consent click** | First external bridge tool call shows blocking Approve/Deny (FR-T1); not exercisable headless | One Approve in Window or dialog |

Full row-level evidence: `docs/production-capability-audit.md`. Cycle log: `docs/ops-loop.md` Rounds 1–6.
