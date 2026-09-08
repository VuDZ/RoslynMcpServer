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
}
