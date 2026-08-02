# Unity-Comdr architecture

## Layers

1. **Protocol** — `UnityComdr.McpHost.McpServer`  
   JSON-RPC 2.0 over newline-delimited stdio.  
   Methods: `initialize`, `tools/*`, `resources/*`, `prompts/*`, `ping`.

2. **Composition** — `UnityComdr.Bootstrap.ComdrRuntime`  
   Builds `ToolRegistry`, domain skills, escape hatches, `ResourceCatalog`, `PromptCatalog`.

3. **Tools** — `UnityComdr.Tools.*`  
   Handlers return `ToolResult` with compact JSON content. No UnityEngine dependency.

4. **Editor adapter** — `UnityComdr.Editor.IEditorHost`  
   - `InMemoryEditorHost` — tests + headless host (full surface for competitive parity)  
   - Live `UnityEditorHost` — UPM package window ready; full API bridge is P1

5. **Skills** — `UnityComdr.Skills.DomainSkills` + `SampleSkills`  
   Nine packs: testing, prefab-advanced, playmode, selection, packages, menu, profiling, screenshots, batch.

6. **Resources / Prompts** — `UnityComdr.Mcp.*`  
   CoderGamester-style `unity://` resources and guided prompt workflows.

## Token policy

- Default active tools ≤ `ToolBudget.MaxDefaultCoreTools` (15).
- Hierarchy/logs/assets paginated via `CompactResults`.
- Escape hatches not listed until `escape_hatches_set enabled=true`.
- Competitive breadth lives in skills, not default schema.

## Package layout

```text
packages/com.unitycomdr.mcp/   UPM + multi-client config window
src/UnityComdr.Core/           Shared library (tools/skills/mcp/editor)
src/UnityComdr.McpHost/        Process entry
tests/UnityComdr.Tests/        Automated verification (21 tests)
docs/competitive-audit-full.md Full vs-Coplay/Ivan/Coder matrix
```

## Competitive alignment map

See [competitive-audit-full.md](competitive-audit-full.md) for the complete capability matrix against CoplayDev, IvanMurzak, CoderGamester, high tool-count forks, and Unity official MCP.

Product/UX direction (vision acceptance, install UX, resilience contract): [product-ux-frontier.md](product-ux-frontier.md).
