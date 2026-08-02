# Unity-Comdr Launch-Readiness Checklist

**Superseded detail for capability rows:** see **[`docs/production-capability-audit.md`](production-capability-audit.md)** (production-grade PASS/FAIL/RESIDUAL).

**Date:** 2026-08-02  
**Decision:** **GO** for Editor MCP production surface on shared host path; Live Editor E2E remains operator-side residual only.

## Quick operator go-live

1. UPM: `packages/com.unitycomdr.mcp`  
2. `dotnet build UnityComdr.sln -c Release`  
3. `dotnet test UnityComdr.sln -c Release`  
4. MCP client → `dotnet exec …/UnityComdr.McpHost.dll`  
5. Optional live: open Unity (bridge `127.0.0.1:17890`)  
6. Optional CI: `UNITY_COMDR_FORCE_HEADLESS=1`  

## Summary gate

| Axis | Status |
|------|--------|
| Install / README | PASS |
| Core + 9 skills non-stub (CallAsync) | PASS — production audit §1–2 |
| Three full loops | PASS |
| Live bridge properties + parity tests | PASS |
| Live Editor E2E in CI sandbox | RESIDUAL (env) |
| Security defaults | PASS |
| Runtime MCP | Out of Editor product surface |

Full row-level evidence: `docs/production-capability-audit.md`.
