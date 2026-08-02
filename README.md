# Unity-Comdr MCP

**Local-first Unity Editor MCP for solo developers** — token-frugal core tools, on-demand skills, MCP resources & prompts, no Python / Node / cloud account required.

| | |
|--|--|
| Package | `com.unitycomdr.mcp` **0.2.0** |
| Host | `UnityComdr.McpHost` (.NET 8, stdio JSON-RPC) |
| Default tools | **≤ 15** core entrypoints |
| Skills | **9** domain packs (load on demand) |
| License | MIT |

## Why (vs popular Unity MCPs)

| Project | Strength we absorb | What we deliberately change |
|---------|-------------------|-----------------------------|
| **CoplayDev/unity-mcp** (~13k★) | Scene/script/test surface, tool groups idea, client configure UX | No Python/`uv`; default tool count kept tiny |
| **IvanMurzak/Unity-MCP** (~3.8k★) | Skills, packages, playmode, profiler, screenshots, reflection | Skills on-demand; no forced cloud; Runtime = P2 |
| **CoderGamester/mcp-unity** (~1.9k★) | Resources, prompts, menu items, batch, project config patterns | No Node; same capabilities as skills + MCP resources/prompts |
| High tool-count forks | Breadth checklist | Breadth via **skill catalog**, not 200 always-on tools |

Full matrix: [`docs/competitive-audit-full.md`](docs/competitive-audit-full.md)  
**Launch go/no-go:** [`docs/launch-readiness.md`](docs/launch-readiness.md)  
**Production capability audit (all Editor skills/core):** [`docs/production-capability-audit.md`](docs/production-capability-audit.md)  
Security defaults: [`SECURITY.md`](SECURITY.md)

## Go-live (local open-source)

Complete operator path (no prior session needed):

| Step | Action |
|------|--------|
| 1 | Install UPM package from `packages/com.unitycomdr.mcp` (file: or git `?path=/packages/com.unitycomdr.mcp`) |
| 2 | Build host: `dotnet build UnityComdr.sln -c Release` |
| 3 | Gate: `dotnet test UnityComdr.sln -c Release` (must exit 0) |
| 4 | Point MCP client at `src/UnityComdr.McpHost/bin/Release/net8.0/UnityComdr.McpHost.dll` via `dotnet exec` |
| 5 | Optional live Editor: open Unity with package → bridge auto-starts on `127.0.0.1:17890` |
| 6 | Optional CI headless: set `UNITY_COMDR_FORCE_HEADLESS=1` (or leave unset; host falls back if bridge down) |
| 7 | Override bridge port: `UNITY_COMDR_BRIDGE_PORT=17890` |

**Requirements:** Unity 2021.3+ for live Editor path. **No Python, Node, or cloud login** for local control.

### 1) UPM package

Unity → **Window → Package Manager → + → Add package from disk / git URL**

```text
file:C:/Ai/Unity-Comdr/packages/com.unitycomdr.mcp
```

Or:

```text
https://github.com/<you>/Unity-Comdr.git?path=/packages/com.unitycomdr.mcp
```

Open **Window → Unity-Comdr MCP** for bridge status and multi-client config copy.

### 2) Build MCP host

```bash
dotnet build UnityComdr.sln -c Release
dotnet test UnityComdr.sln -c Release
```

Optional self-contained exe:

```bash
dotnet publish src/UnityComdr.McpHost/UnityComdr.McpHost.csproj -c Release -r win-x64 --self-contained true
```

### 3) MCP client config

```json
{
  "mcpServers": {
    "unity-comdr": {
      "command": "dotnet",
      "args": [
        "exec",
        "C:/Ai/Unity-Comdr/src/UnityComdr.McpHost/bin/Release/net8.0/UnityComdr.McpHost.dll"
      ]
    }
  }
}
```

Codex CLI (`config.toml`):

```toml
[mcp_servers.unity-comdr]
command = "dotnet"
args = ["exec", "C:/Ai/Unity-Comdr/src/UnityComdr.McpHost/bin/Release/net8.0/UnityComdr.McpHost.dll"]
```

For headless-only sessions, add env in your MCP client config if supported:

```text
UNITY_COMDR_FORCE_HEADLESS=1
```

## Default core tools (always in context)

| Tool | Role |
|------|------|
| `console_read` / `console_clear` | Code-fix loop (filter + pagination) |
| `script_read` / `script_write` / `script_delete` / `script_list` | C# under `Assets/` |
| `editor_state` / `editor_compile` | Compile / play flags |
| `scene_manage` | get/list/list_opened/create/open/save/unload/set_active (+ additive) |
| `hierarchy_get` | Compact hierarchy + digDeeper |
| `gameobject_manage` | create/get/find/delete/duplicate/rename/transform/parent/tag/layer/primitive |
| `component_manage` | add/get/modify/remove/list_types |
| `assets_manage` | find/materials/prefabs/folder/copy/move/delete/refresh/shaders |
| `skill_manage` | list/load/unload domain skills |
| `escape_hatches_set` | Enable reflect/execute (default **off**) |

## Skills (load only what you need)

```text
skill_manage action=list
skill_manage action=load id=playmode
skill_manage action=unload id=playmode
```

| Skill id | Tools | Aligns with |
|----------|-------|-------------|
| `testing` | `tests_run`, `tests_list` | Ivan / Coder test runner |
| `prefab-advanced` | `prefab_batch_instantiate`, `prefab_list` | Ivan prefab suite |
| `playmode` | `playmode_control` | Coder / Ivan play mode |
| `selection` | `selection_manage` | Ivan selection |
| `packages` | `package_manage` | Ivan / Coder UPM |
| `menu` | `menu_manage` | Coder `execute_menu_item` |
| `profiling` | `profiler_manage` | Ivan profiler suite |
| `screenshots` | `screenshot_capture` | Ivan screenshots |
| `batch` | `batch_execute` | Coder batch_execute |

## MCP Resources

| URI | Content |
|-----|---------|
| `unity://console` | Compact logs |
| `unity://hierarchy` | Hierarchy summary |
| `unity://scene` | Active scene |
| `unity://editor-state` | Compile / play |
| `unity://packages` | UPM list |
| `unity://assets` | Asset index |
| `unity://skills` | Skill catalog + load state |
| `unity://selection` | Selection |
| `unity://menu-items` | Menu catalog |

## MCP Prompts

| Name | Purpose |
|------|---------|
| `code_fix_loop` | Console → script → compile → verify |
| `scene_build_loop` | Scene → GO → components → save |
| `playmode_verify_loop` | Play → screenshot/state → stop → fix |
| `skill_expansion` | Load only needed skills |

## Escape hatches (off by default)

```text
escape_hatches_set enabled=true
```

- `reflect_call` — dry-run in headless; live reflection under Editor host (P1)
- `execute_code` — dry-run in headless; Roslyn under Editor host (P1)

## Full agent loops

| Loop | Tools path | Test |
|------|------------|------|
| **Code-fix** | `console_read` → `script_write` → `editor_compile` → `console_read` | `FullLoop_CodeFix_*` |
| **Scene-build** | `scene_manage` → `gameobject_manage` → `component_manage` → `assets_manage` → `hierarchy_get` | `FullLoop_SceneBuild_*` |
| **Playmode-verify** | `skill_manage load playmode` → `playmode_control` play/pause/stop/step → `screenshot_capture` | `FullLoop_PlaymodeVerify_*` |

## Live Unity bridge

When the UPM package is loaded, **LiveUnityBridgeServer** listens on `127.0.0.1:17890`. The MCP host (`EditorHostFactory`) prefers **BridgeClientEditorHost** (same `IEditorHost` as headless). If the Editor is not running, it falls back to **InMemoryEditorHost** (CI-safe).

- Env: `UNITY_COMDR_FORCE_HEADLESS=1` · `UNITY_COMDR_BRIDGE_PORT=17890`
- Status: [docs/full-flow-status.md](docs/full-flow-status.md)

## Architecture

```text
MCP client  --stdio JSON-RPC-->  UnityComdr.McpHost
                                      |
                         ComdrRuntime / ToolRegistry
                         Resources + Prompts
                                      |
                               IEditorHost
              InMemoryEditorHost    BridgeClientEditorHost
                                           |
                                  TCP :17890 (JSON lines)
                                           |
                              LiveUnityBridgeServer (Unity Editor)
```

Docs:

- [Product & UX frontier (design probe, 2026-08-03)](docs/product-ux-frontier.md)
- [Competitive audit (full)](docs/competitive-audit-full.md)
- [Architecture](docs/architecture.md)
- [Transport spike](docs/spike-transport.md)
- [Requirements](docs/brainstorms/2026-08-02-unity-comdr-mcp-requirements.md)
- [Audit notes](docs/audit.md)

## Development

```bash
dotnet test
```

```text
packages/com.unitycomdr.mcp/   UPM
src/UnityComdr.Core/           Handlers, skills, resources, adapters
src/UnityComdr.McpHost/        MCP stdio entry
tests/UnityComdr.Tests/        21 automated tests (handlers + process + parity)
docs/                          Product + competitive audit
```

## Attribution

Capability design informed by CoplayDev, IvanMurzak, and CoderGamester open-source projects. Implementation is original C# under MIT. See [NOTICE](NOTICE) and [THIRD_PARTY.md](THIRD_PARTY.md).

## License

MIT — see [LICENSE](LICENSE).
