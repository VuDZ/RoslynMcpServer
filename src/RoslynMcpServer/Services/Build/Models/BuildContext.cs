using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Build;

/// <summary>
/// One build identity inside a watcher generation. Configuration, platform, TFM,
/// build arguments, and output path are part of the key. The same in-memory solution
/// with a different value here is a different build.
/// </summary>
internal sealed class BuildContext : IEquatable<BuildContext>
{
    public string? Configuration { get; }

    public string? Platform { get; }

    public string? TargetFramework { get; }

    public string? BuildArgs { get; }

    public string? OutputPath { get; }

    public BuildContext(
        string? configuration,
        string? platform,
        string? targetFramework,
        string? buildArgs,
        string? outputPath)
    {
        Configuration = BlankToNull(configuration);
        Platform = BlankToNull(platform);
        TargetFramework = BlankToNull(targetFramework);
        BuildArgs = BlankToNull(buildArgs);
        OutputPath = NormalizeOutputPath(outputPath);
    }

    public bool Equals(BuildContext? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(Configuration, other.Configuration, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Platform, other.Platform, StringComparison.OrdinalIgnoreCase)
            && string.Equals(TargetFramework, other.TargetFramework, StringComparison.OrdinalIgnoreCase)
            && string.Equals(BuildArgs, other.BuildArgs, StringComparison.Ordinal)
            && InputPathCanon.Comparer.Equals(OutputPath, other.OutputPath);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as BuildContext);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Configuration, StringComparer.OrdinalIgnoreCase);
        hash.Add(Platform, StringComparer.OrdinalIgnoreCase);
        hash.Add(TargetFramework, StringComparer.OrdinalIgnoreCase);
        hash.Add(BuildArgs, StringComparer.Ordinal);
        hash.Add(OutputPath, InputPathCanon.Comparer);
        return hash.ToHashCode();
    }

    private static string? BlankToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? NormalizeOutputPath(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return null;
        }

        return InputPathCanon.TryCanonicalize(outputPath) ?? outputPath.Trim();
    }
}
