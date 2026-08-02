# Spike: MCP transport & host model

**Date:** 2026-08-02  
**Decision:** Self-contained **C# MCP host over stdio** (JSON-RPC 2.0 / MCP), packaged with the UPM tree. Optional local HTTP listener for multi-client later; not required for MVP.

## Options considered

| Option | Pros | Cons |
|--------|------|------|
| A. User-installed Python bridge (Coplay-style) | Mature ecosystem | Violates “no Python” UX |
| B. User-installed Node bridge (CoderGamester-style) | Fast TS MCP SDKs | Violates “no Node” UX |
| C. **In-package C# host (stdio)** | Same language as Editor tools; unit-testable with `dotnet`; zero user runtimes | Need lightweight JSON-RPC; Editor talks via named pipe/file/HTTP localhost when in-process is unavailable |
| D. Vendored native binary only | No SDK needed at runtime | Harder to maintain/port |

## Chosen architecture

```
MCP Client (Cursor/Claude/…)
        │ stdio JSON-RPC (MCP)
        ▼
  UnityComdr.McpHost  (console app / net8.0)
        │ tool registry + skills
        ▼
  IEditorHost adapter
        ├── InMemoryEditorHost  (tests / headless)
        └── UnityEditorHost     (UPM Editor scripts; live Unity)
```

- **Default path:** host process launched by client config (`command` + `args`); no cloud, no login.
- **Unity package** provides Editor window, bridge that can attach a live `UnityEditorHost`, and documents pointing the client at the built host DLL/exe (or `dotnet run` for contributors).
- **Headless verification:** `InMemoryEditorHost` + same handlers/registry as production.

## Spike proof

- `dotnet test` on `tests/UnityComdr.Tests` exercises skill registry, core tool budget, P0 handlers.
- Host entry supports `initialize` + `tools/list` + `tools/call` over stdio; launch scripted twice for consistency.

## Limits

- Live Unity API calls require the Editor; CI/sandbox without Unity uses the in-memory adapter (same interfaces as production).
