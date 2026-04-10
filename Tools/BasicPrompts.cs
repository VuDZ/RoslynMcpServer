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
3. If a build fails due to missing references, DO NOT rewrite C# files. Modify the `.csproj` with `update_file_content` or `apply_patch` to add `<ProjectReference>` or `<PackageReference>`.";
        }

        return $"You are a senior C# refactoring assistant. Focus area: {focus}. Preserve behavior, keep changes minimal, and prioritize compile-safe updates.";
    }
}
