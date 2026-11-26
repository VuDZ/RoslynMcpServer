using System.Diagnostics;
using System.Globalization;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>Finds evaluated compiler inputs for a BOM-free patch without opening a workspace.</summary>
internal static class UnloadedSourceReaders
{
    private const int MaxDirectories = 256;
    private const int MaxProjects = 24;
    private const int MaxSolutions = 24;
    private const int MaxEvaluations = 96;
    private const int MaxPathChecks = 8192;

    /// <returns>An empty array when no project was found, or a null entry when discovery was incomplete.</returns>
    internal static int?[] GetCodePages(string sourcePath, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var pages = new HashSet<int?>();
        try
        {
            var knownAliases = new Dictionary<string, bool>(InputPathCanon.Comparer);
            Func<string, bool> isAliased = path => HasPathAlias(path, knownAliases, stopwatch, cancellationToken);
            if (isAliased(sourcePath))
            {
                return [null];
            }

            var search = FindProjects(sourcePath, stopwatch, cancellationToken);
            var pending = new Queue<(string Path, BuildContext Context)>();
            var contexts = new Dictionary<string, HashSet<BuildContext>>(InputPathCanon.Comparer);
            foreach (var path in search.Projects)
            {
                Enqueue(path, default);
            }

            foreach (var solution in search.Solutions)
            {
                CheckBudget(stopwatch, cancellationToken);
                foreach (var project in SolutionFile.Parse(solution).ProjectsInOrder)
                {
                    if (string.Equals(Path.GetExtension(project.AbsolutePath), ".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        Enqueue(project.AbsolutePath, default);
                        foreach (var (key, mapping) in project.ProjectConfigurations)
                        {
                            CheckBudget(stopwatch, cancellationToken);
                            Enqueue(
                                project.AbsolutePath,
                                new BuildContext(mapping.ConfigurationName, mapping.PlatformName, solution, key));
                        }
                    }
                }
            }

            if (pending.Count == 0)
            {
                return [];
            }

            // A fresh collection sees changed imports, globs, and condition inputs on each patch.
            using var collection = new ProjectCollection();
            var evaluations = 0;
            while (pending.TryDequeue(out var candidate))
            {
                var (path, context) = candidate;
                var properties = context.CreateProperties();
                var project = Evaluate(path, properties);
                FindReferences(project, context);
                if (context == default)
                {
                    var platforms = GetVariants(project, "Platform", "Platforms");
                    foreach (var configuration in GetVariants(project, "Configuration", "Configurations"))
                    {
                        foreach (var platform in platforms)
                        {
                            Enqueue(path, new BuildContext(configuration, platform));
                        }
                    }
                }

                ReadFrameworks(project, context, properties);
            }

            CheckBudget(stopwatch, cancellationToken);

            void Enqueue(string path, BuildContext context)
            {
                if (!contexts.TryGetValue(path, out var seen))
                {
                    CheckBudget(stopwatch, cancellationToken);
                    if (contexts.Count >= MaxProjects || isAliased(path))
                    {
                        throw new InvalidOperationException();
                    }

                    seen = [];
                    contexts.Add(path, seen);
                }

                if (seen.Add(context))
                {
                    if (pending.Count >= MaxEvaluations)
                    {
                        throw new TimeoutException();
                    }

                    pending.Enqueue((path, context));
                }
            }

            void FindReferences(Project project, BuildContext context)
            {
                if (context.SolutionPath is not null && UsesUnmodelledSolutionConfiguration(project))
                {
                    pages.Add(null);
                }

                foreach (var reference in project.GetItems("ProjectReference"))
                {
                    var path = InputPathCanon.TryCanonicalize(reference.GetMetadataValue("FullPath"));
                    if (path is null || _referenceOverrides.Any(name => reference.GetMetadataValue(name).Length != 0))
                    {
                        // Evaluated items describe the ordinary build graph. A reference can supply
                        // different global properties or targets when the project actually runs.
                        pages.Add(null);
                        continue;
                    }

                    if (context.SolutionPath is null)
                    {
                        Enqueue(path, default);
                        Enqueue(path, context);
                    }
                    else if (!contexts.TryGetValue(path, out var childContexts)
                        || !childContexts.Any(child => InputPathCanon.Comparer.Equals(child.SolutionPath, context.SolutionPath)
                            && string.Equals(child.SolutionConfiguration, context.SolutionConfiguration, StringComparison.OrdinalIgnoreCase)))
                    {
                        // The solution assigns its own configuration and platform to each referenced
                        // project. Its mapped contexts are already queued. There is no reliable
                        // inherited context for a reference missing from that mapping.
                        pages.Add(null);
                    }
                }
            }

            void ReadFrameworks(Project project, BuildContext context, Dictionary<string, string> properties)
            {
                var frameworks = Split(project.GetPropertyValue("TargetFrameworks"));
                if (frameworks.Length == 0 || project.GetPropertyValue("TargetFramework").Length != 0)
                {
                    AddReader(project, sourcePath, pages, isAliased);
                    return;
                }

                // The cross-targeting outer project dispatches to the inner builds. Its Compile and
                // CodePage values are not inputs to a compiler invocation.
                foreach (var framework in frameworks)
                {
                    var frameworkProperties = new Dictionary<string, string>(properties) { ["TargetFramework"] = framework };
                    var inner = Evaluate(project.FullPath, frameworkProperties);
                    FindReferences(inner, context);
                    AddReader(inner, sourcePath, pages, isAliased);
                }
            }

            Project Evaluate(string path, Dictionary<string, string> properties)
            {
                CheckBudget(stopwatch, cancellationToken);
                if (++evaluations > MaxEvaluations)
                {
                    throw new TimeoutException();
                }

                return collection.LoadProject(path, properties, toolsVersion: null);
            }
        }
        catch (Exception ex) when (ex is InvalidProjectFileException or IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or NotSupportedException or TimeoutException)
        {
            pages.Add(null);
        }

        return [.. pages];
    }

    private static ProjectSearch FindProjects(string sourcePath, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var searchRoot = FindSearchRoot(Path.GetDirectoryName(sourcePath)!, stopwatch, cancellationToken);
        var search = new ProjectSearch();
        if (searchRoot is null)
        {
            return search;
        }

        var pending = new Stack<string>();
        pending.Push(searchRoot);
        var directories = 0;
        while (pending.TryPop(out var directory))
        {
            CheckBudget(stopwatch, cancellationToken);
            AddPaths(Directory.EnumerateFiles(directory, "*.csproj", _topDirectory), search.Projects, MaxProjects);
            AddPaths(EnumerateSolutions(directory), search.Solutions, MaxSolutions);
            directories++;

            foreach (var child in Directory.EnumerateDirectories(directory, "*", _topDirectory))
            {
                if (WorkspaceDiskPathFilter.IsIgnoredPath(child, searchRoot))
                {
                    continue;
                }

                // An alias could leave the search boundary or point back into it.
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0
                    || pending.Count + directories >= MaxDirectories)
                {
                    throw new InvalidOperationException();
                }

                pending.Push(child);
            }
        }

        return search;
    }

    private static string? FindSearchRoot(string sourceDirectory, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        string? solutionRoot = null;
        string? sdkRoot = null;
        string? projectRoot = null;
        for (var current = new DirectoryInfo(sourceDirectory); current.Parent is not null; current = current.Parent)
        {
            CheckBudget(stopwatch, cancellationToken);
            var directory = current.FullName;
            if (Path.Exists(Path.Combine(directory, ".git")))
            {
                return directory;
            }

            if (EnumerateSolutions(directory).Any())
            {
                solutionRoot = directory;
            }

            if (sdkRoot is null && File.Exists(Path.Combine(directory, "global.json")))
            {
                sdkRoot = directory;
            }

            if (Directory.EnumerateFiles(directory, "*.csproj", _topDirectory).Any())
            {
                projectRoot = directory;
            }
        }

        // Even without Git, a nested solution or SDK marker cannot rule out a linked reader
        // in an enclosing solution. An enclosing standalone project can also include the source.
        var root = solutionRoot ?? sdkRoot;
        return root is null || (projectRoot is not null
                && WorkspaceDiskPathFilter.IsPathUnderDirectory(root, projectRoot, InputPathCanon.Comparison))
            ? projectRoot
            : root;
    }

    private static IEnumerable<string> EnumerateSolutions(string directory) =>
        Directory.EnumerateFiles(directory, "*.sln", _topDirectory)
            .Concat(Directory.EnumerateFiles(directory, "*.slnx", _topDirectory));

    private static void AddPaths(IEnumerable<string> paths, HashSet<string> discovered, int limit)
    {
        foreach (var path in paths)
        {
            if (discovered.Count >= limit || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException();
            }

            discovered.Add(path);
        }
    }

    private static void CheckBudget(Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stopwatch.Elapsed >= _budget)
        {
            throw new TimeoutException();
        }
    }

    private static IEnumerable<string?> GetVariants(Project project, string property, string listProperty)
    {
        var values = Split(project.GetPropertyValue(listProperty))
            .Append(project.GetPropertyValue(property))
            .Concat(project.ConditionedProperties.TryGetValue(property, out var conditioned) ? conditioned : [])
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? [null] : values;
    }

    private static bool HasPathAlias(
        string path,
        Dictionary<string, bool> knownAliases,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        // A Windows device namespace can name an ordinary file without setting ReparsePoint.
        if (OperatingSystem.IsWindows() && (path.StartsWith(@"\\.\", StringComparison.Ordinal)
                || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal)))
        {
            return true;
        }

        if (knownAliases.TryGetValue(path, out var known))
        {
            return known;
        }

        // MSBuild's FullPath is lexical. A different item path may still read the file the
        // writer would replace, including through a symlink or a Windows junction above it.
        var uncheckedPaths = new Stack<string>();
        var hasAlias = false;
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if (knownAliases.TryGetValue(current, out hasAlias))
            {
                break;
            }

            CheckBudget(stopwatch, cancellationToken);
            if (knownAliases.Count + uncheckedPaths.Count >= MaxPathChecks)
            {
                throw new TimeoutException();
            }

            uncheckedPaths.Push(current);
            hasAlias = (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0;
            if (hasAlias)
            {
                break;
            }
        }

        foreach (var entry in uncheckedPaths)
        {
            knownAliases.Add(entry, hasAlias);
        }

        return hasAlias;
    }

    private static void AddReader(Project project, string sourcePath, HashSet<int?> pages, Func<string, bool> isAliased)
    {
        if (HasUnresolvedCompilerInputs(project))
        {
            pages.Add(null);
        }

        foreach (var item in project.GetItems("Compile"))
        {
            var path = InputPathCanon.TryCanonicalize(item.GetMetadataValue("FullPath"));
            if (InputPathCanon.Comparer.Equals(path, sourcePath))
            {
                var property = project.GetPropertyValue("CodePage");
                pages.Add(ReadCodePage(property));
                return;
            }

            if (path is null || isAliased(path))
            {
                pages.Add(null);
            }
        }
    }

    private static int? ReadCodePage(string value)
    {
        if (value.Length == 0)
        {
            return 0;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) || page < 0)
        {
            return null;
        }

        return page == 0 ? 0 : SourceTextEncoding.WithoutBom(page).CodePage;
    }

    private static bool UsesUnmodelledSolutionConfiguration(Project project)
    {
        // MSBuild normally generates this property during solution traversal. A custom import
        // that uses it can change the reader graph or CodePage even with correct project mappings.
        return UsesSolutionContents(project.Xml)
            || project.Imports.Any(import => !IsInToolset(import.ImportedProject.FullPath)
                && UsesSolutionContents(import.ImportedProject));

        static bool UsesSolutionContents(ProjectRootElement root) =>
            root.OuterElement.Contains("$(CurrentSolutionConfigurationContents)", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnresolvedCompilerInputs(Project project)
    {
        // An overridden compiler can supply a different CodePage directly on Csc, without
        // changing the evaluated property or any Compile items.
        if ((string.Equals(Path.GetExtension(project.FullPath), ".csproj", StringComparison.OrdinalIgnoreCase)
                && (!project.Targets.TryGetValue("CoreCompile", out var compile)
                    || !InputPathCanon.Comparer.Equals(
                        compile.FullPath, Path.Combine(_toolsetDirectory, "Roslyn", "Microsoft.CSharp.Core.targets"))))
            || _compilerOverrides.Any(name => project.GetPropertyValue(name).Length != 0)
            || project.GetLogicalProject().OfType<ProjectUsingTaskElement>().Any(task =>
                IsCompilerTask(task.TaskName) && !IsInToolset(task.ContainingProject.FullPath)))
        {
            return true;
        }

        // SDK targets add generated compile inputs. A custom target can replace the evaluated
        // inputs, project readers, or CodePage, which cannot be established without a build.
        return project.Targets.Values.Any(target =>
            !IsInToolset(target.FullPath)
            && (target.Tasks.Any(task => IsCompilerTask(task.Name))
                || target.Children.OfType<ProjectItemGroupTaskInstance>().Any(group =>
                    group.Items.Any(item => IsSourceReaderItem(item.ItemType)))
                || target.Children.OfType<ProjectPropertyGroupTaskInstance>().Any(group =>
                    group.Properties.Any(property => property.Name.Equals("CodePage", StringComparison.OrdinalIgnoreCase)))
                || target.Tasks.SelectMany(task => task.Outputs).Any(output => output is ProjectTaskOutputItemInstance item
                        && IsSourceReaderItem(item.ItemType)
                    || output is ProjectTaskOutputPropertyInstance property
                        && property.PropertyName.Equals("CodePage", StringComparison.OrdinalIgnoreCase))));
    }

    private static bool IsInToolset(string path) =>
        path.StartsWith(_toolsetDirectory + Path.DirectorySeparatorChar, InputPathCanon.Comparison);

    private static bool IsCompilerTask(string name) =>
        name.Equals("Csc", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".Csc", StringComparison.OrdinalIgnoreCase);

    private static bool IsSourceReaderItem(string name) =>
        name.Equals("Compile", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase);

    private static string[] Split(string values) =>
        values.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private readonly record struct BuildContext(
        string? Configuration,
        string? Platform,
        string? SolutionPath = null,
        string? SolutionConfiguration = null)
    {
        public Dictionary<string, string> CreateProperties()
        {
            var properties = MsBuildWorkspaceProperties.Create(Configuration, Platform);
            if (SolutionPath is { } solution)
            {
                properties["BuildingSolutionFile"] = "true";
                properties["SolutionDir"] = Path.GetDirectoryName(solution) + Path.DirectorySeparatorChar;
                properties["SolutionExt"] = Path.GetExtension(solution);
                properties["SolutionFileName"] = Path.GetFileName(solution);
                properties["SolutionName"] = Path.GetFileNameWithoutExtension(solution);
                properties["SolutionPath"] = solution;
            }

            return properties;
        }
    }

    private sealed class ProjectSearch
    {
        public HashSet<string> Projects { get; } = new(InputPathCanon.Comparer);
        public HashSet<string> Solutions { get; } = new(InputPathCanon.Comparer);
    }

    private static readonly string _toolsetDirectory = Path.GetDirectoryName(typeof(Project).Assembly.Location)!;
    private static readonly string[] _compilerOverrides = ["CompilerResponseFile", "CscToolExe", "CscToolPath", "CscEnvironment"];

    private static readonly string[] _referenceOverrides =
    [
        "AdditionalProperties", "GlobalPropertiesToRemove", "Properties", "SetConfiguration", "SetPlatform",
        "SetTargetFramework", "Targets", "UndefineProperties",
    ];

    private static readonly TimeSpan _budget = TimeSpan.FromSeconds(3);
    private static readonly EnumerationOptions _topDirectory = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        MatchCasing = MatchCasing.CaseInsensitive,
    };
}
