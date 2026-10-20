using System.Collections.Immutable;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace ParallelLoad;

internal static class ExperimentLoadSettings
{
    public static ImmutableDictionary<string, string> Properties(RunOptions options, string? solutionDirectory = null)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Configuration"] = "Debug" };
        if (options.GlobalProperties == "debug-x64") { properties["Platform"] = "x64"; }
        if (solutionDirectory is not null) { properties["SolutionDir"] = solutionDirectory; }
        return ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, properties);
    }

    public static ILogger? Logger(RunOptions options, string name)
    {
        if (options.BinlogDirectory is null) { return null; }
        Directory.CreateDirectory(options.BinlogDirectory);
        return new BinaryLogger
        {
            Parameters = Path.Combine(options.BinlogDirectory, name + ".binlog") + ";ProjectImports=None",
            Verbosity = LoggerVerbosity.Diagnostic,
        };
    }
}
