# Third-party and upstream references

Unity-Comdr is implemented primarily as **original C# code**. It does **not** currently vendor large trees from other repositories. Tool surface and architecture were informed by:

| Project | License | What we learned |
|---------|---------|-----------------|
| [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) | MIT | Focused tool entrypoints, client configure UX, multi-instance awareness (P1) |
| [IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP) | Apache-2.0 | Skills / extensibility, reflection escape hatches, domain packs |
| [CoderGamester/mcp-unity](https://github.com/CoderGamester/mcp-unity) | MIT | Resources-style compact reads, project-local config patterns |

## NuGet

- `System.Text.Json` — MIT (Microsoft)
- `xunit` / `Microsoft.NET.Test.Sdk` — Apache-2.0 / test only

When porting concrete upstream source files in the future, add file-level SPDX headers and update this document.
