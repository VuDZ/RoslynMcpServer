using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Analyzers;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

/// <summary>
/// Records independent MSBuild observations in an owned fixture. Observing a file is not a closure proof;
/// missing restore, toolset, or executable dependency coverage keeps the entire request unknown.
/// </summary>
internal sealed class DependencyEvidenceRunner
{
    public async Task<AdmissionEvidence> InspectAsync(
        ExperimentRequest request,
        string artifactDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetDirectoryName(Path.GetFullPath(request.WorkspacePath))!;
        RequireOwnedFixture(root);
        Directory.CreateDirectory(artifactDirectory);
        var entries = new List<EvidenceEntry>();
        var instances = new List<InstanceKey>();
        var regions = new List<RegionEntry>();
        var inputs = new HashSet<string>(_paths);
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        var unsupported = false;
        try
        {
            foreach (var project in request.SelectedRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(project))
                {
                    reasons.Add("missing-root");
                    continue;
                }
                var xml = ReadXml(project);
                var sdk = xml.Root?.Attribute("Sdk")?.Value;
                if (sdk is not ("Microsoft.NET.Sdk" or "Microsoft.NET.Sdk.Razor" or "Microsoft.NET.Sdk.Web"))
                {
                    unsupported = true;
                    reasons.Add("non-sdk-project");
                    continue;
                }
                var name = Path.GetFileNameWithoutExtension(project);
                var evaluation = await RunAsync(root, artifactDirectory, name + "-evaluation",
                    ["msbuild", project, "-nologo", "-getProperty:TargetFramework,TargetFrameworks,DefaultItemExcludes,DefaultExcludesInProjectFolder,TargetPath,MSBuildToolsPath,NETCoreSdkVersion", "-getItem:Compile,AdditionalFiles,Analyzer,ProjectReference,Content"], cancellationToken);
                if (evaluation.ExitCode != 0)
                {
                    reasons.Add("evaluation-failed");
                    continue;
                }
                using var evaluated = JsonDocument.Parse(evaluation.StandardOutput);
                var properties = evaluated.RootElement.GetProperty("Properties");
                var frameworks = request.TargetFramework is not null
                    ? new[] { request.TargetFramework }
                    : properties.GetProperty("TargetFrameworks").GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries);
                if (frameworks.Length == 0)
                {
                    frameworks = [properties.GetProperty("TargetFramework").GetString()!];
                }
                if (frameworks.Any(string.IsNullOrEmpty))
                {
                    reasons.Add("missing-inner-tfm");
                    continue;
                }
                foreach (var framework in frameworks)
                {
                    var effective = ImmutableSortedDictionary<string, string>.Empty
                        .Add("Configuration", request.Configuration ?? "Debug")
                        .Add("Platform", request.Platform ?? "AnyCPU")
                        .Add("TargetFramework", framework);
                    var instance = new InstanceKey(Path.GetFullPath(project), framework, effective);
                    instances.Add(instance);
                    AddFile(entries, "project", instance, project, "project-xml", project);
                    InspectXml(project, xml, reasons, ref unsupported);
                    foreach (var mode in new[] { "None", "Embed" })
                    {
                        foreach (var designTime in new[] { false, true })
                        {
                            var label = $"{name}-{framework}-{(designTime ? "dtb" : "build")}-{mode}";
                            var binlog = Path.Combine(artifactDirectory, label + ".binlog");
                            var arguments = designTime
                                ? new List<string> { "msbuild", project, "-t:Compile", "-p:DesignTimeBuild=true", "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true" }
                                : new List<string> { "build", project, "--no-restore" };
                            arguments.Add("-p:NonExistentFile=" + Path.Combine(artifactDirectory, "never-created-" + label));
                            arguments.AddRange(["-p:Configuration=" + effective["Configuration"], "-p:Platform=" + effective["Platform"], "-p:TargetFramework=" + framework, "-bl:" + binlog + ";ProjectImports=" + mode, "-v:diag"]);
                            var command = await RunAsync(root, artifactDirectory, label, arguments, cancellationToken);
                            if (command.ExitCode != 0)
                            {
                                reasons.Add("build-or-dtb-failed:" + framework);
                            }
                            if (!File.Exists(binlog))
                            {
                                reasons.Add("missing-binlog");
                                continue;
                            }
                            var observed = Replay(binlog, cancellationToken);
                            await WriteJsonAsync(Path.Combine(artifactDirectory, label + "-observed.json"), observed, cancellationToken);
                            if (designTime && mode == "None")
                            {
                                CollectObserved(instance, observed, binlog, entries, inputs, reasons, ref unsupported);
                            }
                        }
                    }
                    regions.AddRange(CreateObservedRegions(instance, sdk, xml, properties,
                        Path.Combine(artifactDirectory, name + "-evaluation.stdout.txt"), cancellationToken));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or XmlException)
        {
            reasons.Add("evidence-failure:" + exception.GetType().Name);
        }

        // Public item/command evidence cannot establish reads performed inside arbitrary SDK tasks,
        // resolver inputs, dynamic analyzer dependencies, or restore selection. Do not invent a Complete snapshot.
        reasons.Add("restore-resolution-closure-unproven");
        reasons.Add("toolset-identity-input-set-unproven");
        reasons.Add("analyzer-dependency-closure-unproven");
        var evidence = new AdmissionEvidence("sdk-project-v1/e0-1", unsupported ? "unsupported" : "unknown",
            entries.Distinct().ToImmutableArray(), regions.ToImmutableArray(), instances.Distinct().ToImmutableArray(), [],
            inputs.Order(_paths).ToImmutableArray(), reasons.ToImmutableArray(), false, false);
        await WriteJsonAsync(Path.Combine(artifactDirectory, "admission.json"), evidence, cancellationToken);
        return evidence;
    }

    public async Task<AnalyzerProvenanceSnapshot?> RevalidateOverlayAsync(
        SemanticSnapshot snapshot,
        AdmissionEvidence evidence,
        Guid newSessionId,
        Solution baseSolution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(baseSolution);
        if (newSessionId == Guid.Empty)
        {
            throw new ArgumentException("A new nonempty session identity is required.", nameof(newSessionId));
        }
        cancellationToken.ThrowIfCancellationRequested();
        await RecheckObservedFilesAsync(evidence.Entries, cancellationToken);
        // No inspected request has independently established the mandatory closure yet. In particular,
        // caller-supplied OverlayReady and hashes are not authority to execute a DLL or fabricate provenance.
        return null;
    }

    internal static async Task<ImmutableArray<string>> RecheckObservedFilesAsync(
        IEnumerable<EvidenceEntry> entries,
        CancellationToken cancellationToken)
    {
        var failures = ImmutableArray.CreateBuilder<string>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (entry.IsAbsent)
                {
                    if (File.Exists(entry.PathOrRegion) || Directory.Exists(entry.PathOrRegion))
                    {
                        failures.Add("known-absent-appeared:" + entry.PathOrRegion);
                    }
                    continue;
                }
                var bytes = await File.ReadAllBytesAsync(entry.PathOrRegion, cancellationToken);
                if (Convert.ToHexString(SHA256.HashData(bytes)) != entry.Sha256)
                {
                    failures.Add("content-changed:" + entry.PathOrRegion);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add("unreadable:" + entry.PathOrRegion);
            }
        }
        return failures.ToImmutable();
    }

    internal static async Task<DependencyCommandResult> RunAsync(
        string workingDirectory,
        string artifactDirectory,
        string label,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(artifactDirectory);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var safeEnvironment = new[] { "SystemRoot", "WINDIR", "PATH", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "ProgramFiles", "ProgramFiles(x86)", "DOTNET_ROOT" };
        start.Environment.Clear();
        foreach (var name in safeEnvironment)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                start.Environment[name] = value;
            }
        }
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new IOException("The dotnet process did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var result = new DependencyCommandResult(process.ExitCode, await output, await error);
            await File.WriteAllTextAsync(Path.Combine(artifactDirectory, label + ".stdout.txt"), result.StandardOutput, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(artifactDirectory, label + ".stderr.txt"), result.StandardError, cancellationToken);
            await WriteJsonAsync(Path.Combine(artifactDirectory, label + "-command.json"),
                new { Executable = start.FileName, start.WorkingDirectory, Arguments = start.ArgumentList.ToArray(), result.ExitCode }, cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    internal static DependencyObservations Replay(string path, CancellationToken cancellationToken)
    {
        var result = new DependencyObservations();
        var replay = new BinaryLogReplayEventSource();
        var contexts = new Dictionary<AnalyzerProvenanceProjectContextKey, string>();
        replay.ProjectStarted += (_, args) =>
        {
            if (args.BuildEventContext is not null)
            {
                contexts[AnalyzerProvenanceProjectContextKey.From(args.BuildEventContext)] = args.ProjectFile ?? "";
            }
            result.ProjectContexts.Add(new DependencyContextObservation(args.ProjectFile ?? "", args.BuildEventContext?.ProjectInstanceId ?? -1,
                args.GlobalProperties?.Where(pair => _allowedProperties.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value) ?? []));
        };
        replay.AnyEventRaised += (_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (args is ProjectImportedEventArgs import)
            {
                result.Imports.Add(new DependencyImportObservation(import.ProjectFile ?? "", import.ImportedProjectFile ?? "",
                    import.UnexpandedProject ?? "", import.ImportIgnored, import.Message ?? ""));
            }
            if (args is TaskParameterEventArgs parameter && parameter.Items is not null)
            {
                if (parameter.Kind == TaskParameterMessageKind.TaskOutput && parameter.ItemType == "Analyzer")
                {
                    foreach (var item in parameter.Items.OfType<ITaskItem>())
                    {
                        result.TaskOutputAnalyzers.Add(item.ItemSpec);
                        result.TaskOutputOwners.Add(parameter.BuildEventContext is not null
                            && contexts.TryGetValue(AnalyzerProvenanceProjectContextKey.From(parameter.BuildEventContext), out var project)
                            ? project : "");
                    }
                }
            }
            if (args is TaskCommandLineEventArgs { TaskName: "Csc" } command)
            {
                result.CompilerCommands.Add(command.CommandLine);
                result.CompilerOwners.Add(command.BuildEventContext is not null
                    && contexts.TryGetValue(AnalyzerProvenanceProjectContextKey.From(command.BuildEventContext), out var project)
                    ? project : "");
            }
            if (args is TargetStartedEventArgs { TargetName: "CoreCompile" })
            {
                result.CoreCompileCount++;
            }
        };
        using var stream = File.OpenRead(path);
        replay.Replay(stream, cancellationToken);
        return result;
    }

    internal static Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, _jsonOptions), cancellationToken);

    private static IEnumerable<RegionEntry> CreateObservedRegions(
        InstanceKey owner,
        string sdk,
        XDocument project,
        JsonElement properties,
        string locator,
        CancellationToken cancellationToken)
    {
        var root = Path.GetDirectoryName(owner.ProjectPath)!;
        var excludes = (properties.GetProperty("DefaultItemExcludes").GetString() + ";"
            + properties.GetProperty("DefaultExcludesInProjectFolder").GetString())
            .Split(';', StringSplitOptions.RemoveEmptyEntries).Select(NormalizePattern).Distinct().ToImmutableArray();
        var compileExcludes = excludes.AddRange(project.Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .SelectMany(element => (element.Attribute("Remove")?.Value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(NormalizePattern));
        var definitions = new List<(string Role, ImmutableArray<string> Includes, ImmutableArray<string> Excludes)>
        {
            ("compile", ["**/*.cs"], compileExcludes),
        };
        foreach (var element in project.Descendants().Where(element => element.Name.LocalName == "AdditionalFiles"))
        {
            var include = element.Attribute("Include")?.Value;
            if (include is not null)
            {
                definitions.Add(("additional", include.Split(';').Select(NormalizePattern).ToImmutableArray(), []));
            }
        }
        if (sdk is "Microsoft.NET.Sdk.Razor" or "Microsoft.NET.Sdk.Web")
        {
            definitions.Add(("razor", ["**/*.razor", "**/*.cshtml"], excludes));
            definitions.Add(("web-asset", ["wwwroot/**"], []));
        }
        var files = new List<string>();
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));
        var visited = 0;
        while (pending.TryPop(out var directory))
        {
            if (directory.Depth > 64)
            {
                throw new IOException("The fixture region depth limit was exceeded.");
            }
            foreach (var path in Directory.EnumerateFileSystemEntries(directory.Directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++visited > 100000 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("The fixture region entry limit or reparse-point boundary was reached.");
                }
                if (Directory.Exists(path))
                {
                    pending.Push((path, directory.Depth + 1));
                }
                else
                {
                    files.Add(path);
                }
            }
        }
        long bytesRead = 0;
        foreach (var definition in definitions)
        {
            var includes = definition.Includes.Select(CompileGlob).ToArray();
            var excluded = definition.Excludes.Select(CompileGlob).ToArray();
            var members = ImmutableArray.CreateBuilder<RegionMember>();
            foreach (var path in files.Order(_paths))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (!includes.Any(pattern => pattern.IsMatch(relative)) || excluded.Any(pattern => pattern.IsMatch(relative)))
                {
                    continue;
                }
                bytesRead += new FileInfo(path).Length;
                if (bytesRead > 256 * 1024 * 1024)
                {
                    throw new IOException("The fixture region byte limit was exceeded.");
                }
                members.Add(new RegionMember(relative, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
            }
            var manifest = "e0-region-1\n" + string.Concat(members.Select(member =>
                JsonSerializer.Serialize(new[] { member.RelativePath, member.Sha256 }) + "\n"));
            yield return new RegionEntry(owner, root, definition.Role, definition.Includes, definition.Excludes,
                members.ToImmutable(), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))), locator);
        }
    }

    private static string NormalizePattern(string pattern) => pattern.Replace('\\', '/').Replace("//", "/", StringComparison.Ordinal);

    private static Regex CompileGlob(string pattern)
    {
        if (Path.IsPathRooted(pattern) || pattern.Split('/').Any(segment => segment is "." or "..")
            || pattern.Contains('$') || pattern.Contains('@') || pattern.Contains('[') || pattern.Contains(']'))
        {
            throw new IOException("An observed region pattern is not bounded and representable.");
        }
        var expression = new StringBuilder("^");
        var segments = pattern.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment == "**")
            {
                expression.Append(index == segments.Length - 1 ? ".*" : "(?:[^/]+/)*");
                continue;
            }
            expression.Append(Regex.Escape(segment).Replace("\\*", "[^/]*", StringComparison.Ordinal)
                .Replace("\\?", "[^/]", StringComparison.Ordinal));
            if (index != segments.Length - 1)
            {
                expression.Append('/');
            }
        }
        return new Regex(expression.Append('$').ToString(), OperatingSystem.IsWindows()
            ? RegexOptions.CultureInvariant | RegexOptions.IgnoreCase : RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private static void CollectObserved(
        InstanceKey owner,
        DependencyObservations observed,
        string binlog,
        List<EvidenceEntry> entries,
        HashSet<string> inputs,
        SortedSet<string> reasons,
        ref bool unsupported)
    {
        var sdkImports = observed.Imports.Where(import => import.Imported.EndsWith("Sdk.props", StringComparison.OrdinalIgnoreCase))
            .Select(import => import.Imported).ToArray();
        if (sdkImports.Length == 0)
        {
            reasons.Add("sdk-import-missing");
        }
        foreach (var import in observed.Imports)
        {
            if (File.Exists(import.Imported))
            {
                var importPath = Path.GetFullPath(import.Imported);
                AddFile(entries, "import", null, importPath, "ProjectImportedEventArgs-unbound-owner", binlog);
                var sdkRoot = Path.GetDirectoryName(typeof(Microsoft.Build.Evaluation.Project).Assembly.Location)!;
                if (!importPath.StartsWith(sdkRoot + Path.DirectorySeparatorChar, _pathComparison))
                {
                    InspectXml(importPath, ReadXml(importPath), reasons, ref unsupported);
                }
                continue;
            }
            if (string.IsNullOrEmpty(import.Unexpanded) || import.Unexpanded.Contains("$(", StringComparison.Ordinal)
                || import.Unexpanded.Contains('*') || string.IsNullOrEmpty(import.Project))
            {
                reasons.Add("unresolved-negative-import");
                continue;
            }
            var path = Path.GetFullPath(import.Unexpanded, Path.GetDirectoryName(import.Project)!);
            var sourceImport = File.Exists(import.Project) ? ReadXml(import.Project).Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Import"
                    && element.Attribute("Project")?.Value == import.Unexpanded) : null;
            var expectedCondition = "Exists('" + import.Unexpanded + "')";
            if (sourceImport?.Attribute("Condition")?.Value.Trim() != expectedCondition)
            {
                reasons.Add("unresolved-negative-import");
                continue;
            }
            if (!File.Exists(path))
            {
                entries.Add(new EvidenceEntry("known-absent", null, path, null, true,
                    "ProjectImportedEventArgs", "MSBuild-18.6.3", binlog));
            }
        }
        for (var commandIndex = 0; commandIndex < observed.CompilerCommands.Count; commandIndex++)
        {
            var command = observed.CompilerCommands[commandIndex];
            if (!_paths.Equals(observed.CompilerOwners[commandIndex], owner.ProjectPath))
            {
                reasons.Add("transitive-compiler-context-requires-separate-inner-inspection");
                continue;
            }
            var start = command.IndexOf(" /noconfig", StringComparison.Ordinal);
            if (start < 0)
            {
                reasons.Add("compiler-command-format-unknown");
                continue;
            }
            var arguments = Microsoft.CodeAnalysis.CSharp.CSharpCommandLineParser.Default.Parse(
                Microsoft.CodeAnalysis.CommandLineParser.SplitCommandLineIntoArguments(command[(start + 1)..], false),
                Path.GetDirectoryName(owner.ProjectPath)!, null, null);
            foreach (var source in arguments.SourceFiles)
            {
                inputs.Add(source.Path);
                AddFile(entries, "compile", owner, source.Path, "Csc-command-line", binlog);
            }
            foreach (var additional in arguments.AdditionalFiles)
            {
                inputs.Add(additional.Path);
                AddFile(entries, "additional", owner, additional.Path, "Csc-command-line", binlog);
            }
            foreach (var config in arguments.AnalyzerConfigPaths)
            {
                inputs.Add(config);
                AddFile(entries, "analyzer-config", owner, config, "Csc-command-line", binlog);
            }
            foreach (var analyzer in arguments.AnalyzerReferences)
            {
                var path = Path.GetFullPath(analyzer.FilePath, Path.GetDirectoryName(owner.ProjectPath)!);
                AddFile(entries, "analyzer", owner, path, "Csc-command-line", binlog);
                if (!observed.TaskOutputAnalyzers.Select((item, index) => new { Item = item, Owner = observed.TaskOutputOwners[index] })
                    .Any(item => _paths.Equals(item.Owner, owner.ProjectPath)
                        && _paths.Equals(Path.GetFullPath(item.Item, Path.GetDirectoryName(owner.ProjectPath)!), path)))
                {
                    reasons.Add("compiler-analyzer-missing-taskoutput");
                }
            }
            foreach (var reference in arguments.MetadataReferences)
            {
                AddFile(entries, "metadata", owner, reference.Reference, "Csc-command-line", binlog);
            }
        }
        if (observed.CompilerCommands.Count == 0)
        {
            reasons.Add("compiler-input-inventory-missing");
        }
    }

    private static void InspectXml(string path, XDocument document, SortedSet<string> reasons, ref bool unsupported)
    {
        if (document.Descendants().Any(element => element.Name.LocalName == "Target"))
        {
            reasons.Add("custom-target");
        }
        foreach (var item in document.Descendants())
        {
            var include = item.Attribute("Include")?.Value;
            if (include is null)
            {
                continue;
            }
            if ((include.Contains('*') || include.Contains('?')) && (Path.IsPathRooted(include) || include.Replace('\\', '/').Contains("../", StringComparison.Ordinal)))
            {
                unsupported = true;
                reasons.Add("escaping-glob");
            }
            if (item.Name.LocalName == "Content" && !include.Contains("$(", StringComparison.Ordinal)
                && !include.Contains('*') && !include.Contains('?')
                && !Path.GetFullPath(include, Path.GetDirectoryName(path)!).StartsWith(Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar, _pathComparison))
            {
                reasons.Add("external-static-asset");
            }
        }
    }

    private static XDocument ReadXml(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static void AddFile(List<EvidenceEntry> entries, string category, InstanceKey? owner, string path, string source, string locator)
    {
        path = owner is null ? Path.GetFullPath(path) : Path.GetFullPath(path, Path.GetDirectoryName(owner.ProjectPath)!);
        if (!File.Exists(path))
        {
            throw new IOException("An observed compiler or import input is unavailable.");
        }
        entries.Add(new EvidenceEntry(category, owner, path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            false, source, "sdk-10.0.300/msbuild-18.6.3", locator));
    }

    private static void RequireOwnedFixture(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, ".dependency-spike-owned")))
            {
                return;
            }
        }
        throw new ArgumentException("The runner requires an explicitly owned isolated fixture root.");
    }

    private static readonly StringComparer _paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly HashSet<string> _allowedProperties = ["DesignTimeBuild", "SkipCompilerExecution", "ProvideCommandLineArgs", "Configuration", "Platform", "TargetFramework"];
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
}
