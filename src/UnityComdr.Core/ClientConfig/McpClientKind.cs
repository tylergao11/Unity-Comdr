namespace UnityComdr.ClientConfig;

/// <summary>
/// MCP clients supported by Phase I Install (FR-I1/I2).
/// Pattern port from CoplayDev/unity-mcp Clients/Configurators (Cursor/VS Code/Claude Code/Codex).
/// </summary>
public enum McpClientKind
{
    Cursor = 0,
    VsCode = 1,
    ClaudeCode = 2,
    CodexCli = 3
}
