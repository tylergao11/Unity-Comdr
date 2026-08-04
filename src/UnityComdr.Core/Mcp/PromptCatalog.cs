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
            "Load playmode + screenshots → play → screenshot_capture (MCP image) → judge from image → stop → fix."),
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
            "Retry etiquette (required): script_write/editor_compile often trigger compile or domain reload. " +
            "If a tool returns isError containing editor_compiling, editor_reloading, play_transition, or editor_gone, " +
            "read suggestedRetrySeconds / nextStep, wait, optionally poll editor_state until phase=connected, then retry the same call. " +
            "Never treat a hang, socket timeout, or missing result as success; never invent console/compile outcomes.\n" +
            "Do not ask the user to paste console text.",
        "scene_build_loop" =>
            "You are building a Unity scene via Unity-Comdr MCP.\n" +
            "1) scene_manage action=get or create as needed.\n" +
            "2) hierarchy_get for compact structure.\n" +
            "3) gameobject_manage create (use primitive when useful); component_manage add.\n" +
            "4) assets_manage for materials/prefabs; set_transform with partial axes as needed.\n" +
            "5) Vision checkpoint: skill_manage load screenshots; screenshot_capture source=game_view " +
            "(MCP type:image). Judge assembly from the image (camera framing, objects not stacked at origin).\n" +
            "6) scene_manage action=save.\n" +
            "Prefer compact hierarchy_get over dumping every property. maxResolution=640 is a cost knob for whole-frame only.",
        "playmode_verify_loop" =>
            "You are verifying gameplay via Unity-Comdr MCP with VISION checkpoints (required).\n" +
            "1) skill_manage action=load id=playmode; skill_manage action=load id=screenshots.\n" +
            "2) Confirm editor_state hostMode=live (headless cannot see pixels).\n" +
            "3) playmode_control action=play; optionally step/pause.\n" +
            "4) screenshot_capture source=game_view (returns MCP type:image png). " +
            "Optionally batch=surround target=<go> for one contact sheet, or region crop for UI detail (native res).\n" +
            "5) JUDGE FROM THE IMAGE ALONE (labels visible? spawn? layout). Do not invent pixels from hierarchy text.\n" +
            "6) On failure: screenshot_capture source=scene_view or isolated target=<id>; then fix scene/scripts.\n" +
            "7) playmode_control action=stop; retest until image matches the goal.\n" +
            "Busy etiquette: if editor_compiling/editor_reloading, wait suggestedRetrySeconds and retry.",
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
