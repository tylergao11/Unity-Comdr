# Security

Unity-Comdr is a **local-first** MCP bridge for the Unity Editor.

## Defaults (launch baseline)

- **No cloud login required** for local Editor control.
- **No phone-home telemetry** in the MCP host startup path.
- **Escape hatches** (`reflect_call`, `execute_code`) are **disabled** until `escape_hatches_set enabled=true`.
- Live bridge binds to **127.0.0.1** only (loopback), default port **17890**.
- Destructive operations (delete assets/scripts, batch) require explicit tool arguments.

## Trust surface (FR-T1 / FR-T2 / FR-T3)

- **First-connection consent (blocking):** the first external MCP/bridge client that invokes a non-doctor method must be approved in the Unity Editor (modal dialog). Approval is remembered in **EditorPrefs** (`UnityComdr.BridgeConsentApproved`). Until approved, tool methods are refused with a clear `consent_required` / `consent_denied` error. Doctor probes (`ping`, `editor.getState`) still work. Revoke from **Window → Unity-Comdr MCP**.
- **Per-tool / per-skill disable list:** local JSON at `ProjectSettings/UnityComdr.mcp.json` (`disabledTools`, `disabledSkills`). The MCP host `ToolRegistry.GetActiveTools` filters these out of `tools/list`. Headless can also use `UNITY_COMDR_TRUST_SETTINGS`, `UNITY_COMDR_DISABLED_TOOLS`, and `UNITY_COMDR_DISABLED_SKILLS`.
- **Local invocation audit log:** append-only JSONL at `Temp/unity-comdr-audit.jsonl` (or `Logs/` when no Unity `Temp`/`Assets` layout; override with `UNITY_COMDR_AUDIT_LOG`). Fields: `timestamp`, `tool`, `ok`, `durationMs` (optional `error`). **No phone-home** — file stays on disk. Enabled by default for live bridge tool calls; Core records only when an audit sink is registered.

## Operator advice

- Do not expose the bridge port beyond localhost without authentication (not provided in MVP).
- Treat `execute_code` / reflection as privileged if enabled in a trusted project only.
- Prefer `UNITY_COMDR_FORCE_HEADLESS=1` in CI so agents cannot accidentally attach to a developer Editor.
- Review `Temp/unity-comdr-audit.jsonl` if you need a local trail of what the agent invoked.

## Reporting

Report security issues privately to the repository maintainers. See also `docs/launch-readiness.md` §4.
