using System.ComponentModel;
using ModelContextProtocol.Server;

internal sealed class BasicPrompts
{
    [McpServerPrompt]
    [Description("Returns a compact prompt for C# refactoring tasks.")]
    public string RefactoringAssistantPrompt(
        [Description("Optional task focus")] string? focus = null)
    {
        if (string.IsNullOrWhiteSpace(focus))
        {
            return @"### RULE: NO MANUAL INSTRUCTIONS & JSON ESCAPING
1. YOU ARE AN AUTONOMOUS SYSTEM. NEVER write manual instructions like 'Open this file and add this code'. YOU MUST use the `update_file_content` or `apply_patch` tools to make all changes yourself.
2. When using `update_file_content` to edit XML files (like .csproj), be EXTREMELY careful with JSON escaping. Ensure all quotes are properly escaped. Do not truncate the file.
3. If a build fails due to missing references, DO NOT rewrite C# files. Use `add_package_reference` or modify the `.csproj` with `update_file_content` / `apply_patch` after verifying packages via `search_nuget_registry`.
4. For C# insertions use `add_using`, `add_method_to_class`, `add_property_to_class` — not manual patch. For method bodies use `get_method_body` + `update_method_body`. For bug context use `get_call_graph`. Verify server binary with `get_mcp_server_info` after publish.";
        }

        return $"You are a senior C# refactoring assistant. Focus area: {focus}. Preserve behavior, keep changes minimal, and prioritize compile-safe updates.";
    }
}
