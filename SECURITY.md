# Security

Unity-Comdr is a **local-first** MCP bridge for the Unity Editor.

## Defaults (launch baseline)

- **No cloud login required** for local Editor control.
- **No phone-home telemetry** in the MCP host startup path.
- **Escape hatches** (`reflect_call`, `execute_code`) are **disabled** until `escape_hatches_set enabled=true`.
- Live bridge binds to **127.0.0.1** only (loopback), default port **17890**.
- Destructive operations (delete assets/scripts, batch) require explicit tool arguments.

## Operator advice

- Do not expose the bridge port beyond localhost without authentication (not provided in MVP).
- Treat `execute_code` / reflection as privileged if enabled in a trusted project only.
- Prefer `UNITY_COMDR_FORCE_HEADLESS=1` in CI so agents cannot accidentally attach to a developer Editor.

## Reporting

Report security issues privately to the repository maintainers. See also `docs/launch-readiness.md` §4.
