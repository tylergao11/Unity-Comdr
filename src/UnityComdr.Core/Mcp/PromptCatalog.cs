namespace UnityComdr.Mcp;

/// <summary>
/// MCP Prompts inspired by CoderGamester guided workflows.
/// </summary>
public sealed class PromptCatalog
{
    public IReadOnlyList<PromptDescriptor> List() => new[]
    {
        new PromptDescriptor(
            "code_fix_loop",
            "Code Fix Loop",
            "Guided workflow: read console errors → open scripts → fix → compile → re-read console."),
        new PromptDescriptor(
            "scene_build_loop",
            "Scene Build Loop",
            "Guided workflow: create/open scene → create GO hierarchy → components → materials → save."),
        new PromptDescriptor(
            "playmode_verify_loop",
            "Play Mode Verify Loop",
            "Load playmode skill → enter play → capture screenshot/state → stop → fix issues."),
        new PromptDescriptor(
            "skill_expansion",
            "Skill Expansion",
            "List skills, load only what is needed for the current task, unload when done.")
    };

    public string Get(string name) => name.ToLowerInvariant() switch
    {
        "code_fix_loop" =>
            "You are fixing Unity compile/runtime errors via Unity-Comdr MCP.\n" +
            "1) Call console_read with type=Error (page if hasMore).\n" +
            "2) Identify file paths from messages; script_read those files.\n" +
            "3) Apply script_write fixes; call editor_compile.\n" +
            "4) console_read again until total errors is 0.\n" +
            "Do not ask the user to paste console text.",
        "scene_build_loop" =>
            "You are building a Unity scene via Unity-Comdr MCP.\n" +
            "1) scene_manage action=get or create as needed.\n" +
            "2) hierarchy_get for compact structure.\n" +
            "3) gameobject_manage create (use primitive when useful); component_manage add.\n" +
            "4) assets_manage for materials/prefabs; set_transform with partial axes as needed.\n" +
            "5) scene_manage action=save.\n" +
            "Prefer compact hierarchy_get over dumping every property.",
        "playmode_verify_loop" =>
            "You are verifying gameplay via Unity-Comdr MCP.\n" +
            "1) skill_manage action=load id=playmode (and screenshots if needed).\n" +
            "2) playmode_control action=play; optionally step/pause.\n" +
            "3) screenshot_capture source=game_view; editor_state / console_read.\n" +
            "4) playmode_control action=stop; fix scripts/scene; retest.",
        "skill_expansion" =>
            "Keep the default tool set small.\n" +
            "1) skill_manage action=list — review id/description.\n" +
            "2) Load only skills required for the current task (testing, packages, menu, profiling, batch…).\n" +
            "3) When finished, skill_manage action=unload to reduce token pressure.\n" +
            "Escape hatches (reflect_call/execute_code) stay off unless explicitly needed.",
        _ => throw new InvalidOperationException($"Unknown prompt: {name}")
    };
}

public sealed record PromptDescriptor(string Name, string Title, string Description);
