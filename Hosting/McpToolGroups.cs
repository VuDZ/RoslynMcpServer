namespace RoslynMcpServer.Hosting;

/// <summary>Canonical logical groups for startup profile selection.</summary>
public static class McpToolGroups
{
    public const string Core = "core";
    public const string Files = "files";
    public const string Editing = "editing";
    public const string Decompile = "decompile";
    public const string NuGet = "nuget";
    public const string Project = "project";
    public const string Runtime = "runtime";
    public const string Operations = "operations";

    public static IReadOnlyList<string> All { get; } =
    [
        Core,
        Files,
        Editing,
        Decompile,
        NuGet,
        Project,
        Runtime,
        Operations,
    ];

    public static string Describe(string group) => group switch
    {
        Core => "Workspace, navigation, build, and test essentials.",
        Files => "Disk file read, search, and patch.",
        Editing => "AST edits, code fixes, format, and rename.",
        Decompile => "Inspect third-party assemblies.",
        NuGet => "Package list, audit, search, add, and remove.",
        Project => "Solution graph and project rename.",
        Runtime => "Run apps, list tests, and raw dotnet.",
        Operations => "Logs, scratchpad, and process lifecycle.",
        _ => "Unknown group.",
    };
}
