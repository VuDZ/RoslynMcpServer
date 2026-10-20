using System.Diagnostics;
using System.Xml.Linq;
using ParallelLoad.Bootstrap;

namespace ParallelLoad;

internal static class CoreFirstAggregator
{
    public static string Write(string solutionPath, string temporary, int group, string[] roots)
    {
        var directory = Path.Combine(temporary, $"aggregator-{group}");
        Directory.CreateDirectory(directory);
        var ancestor = new DirectoryInfo(Path.GetDirectoryName(solutionPath)!);
        while (ancestor is not null)
        {
            var global = Path.Combine(ancestor.FullName, "global.json");
            if (File.Exists(global))
            {
                File.Copy(global, Path.Combine(directory, "global.json"));
                break;
            }

            ancestor = ancestor.Parent;
        }

        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(directory, "Directory.Build.targets"), "<Project />");
        var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                new XElement("EnableDefaultItems", "false"), new XElement("NuGetAudit", "false")),
            new XElement("ItemGroup", roots.Select(root => new XElement("ProjectReference",
                new XAttribute("Include", root), new XAttribute("ReferenceOutputAssembly", "false"),
                new XAttribute("SkipGetTargetFrameworkProperties", "true"), new XAttribute("Private", "false")))));
        var path = Path.Combine(directory, "Group.csproj");
        new XDocument(project).Save(path);
        return path;
    }

    public static async Task<string> RestoreAsync(string path, string registration, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(registration, "MSBuild.exe");
        if (!File.Exists(executable))
        {
            var visualStudio = MsBuildInstanceSelector.FindVisualStudioMsBuildDirectory();
            executable = Path.Combine(visualStudio ?? "", "MSBuild.exe");
            if (!File.Exists(executable))
            {
                throw new InvalidOperationException("Aggregator restore requires Visual Studio MSBuild.exe.");
            }
        }

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        };
        foreach (var argument in new[] { path, "-t:Restore", "-p:RestoreRecursive=false", "-p:Configuration=Debug", "-p:Platform=x64", "-verbosity:minimal", "-nologo" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        var text = await output + await error;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Aggregator-only restore failed ({process.ExitCode}): {text}");
        }

        return text;
    }
}
