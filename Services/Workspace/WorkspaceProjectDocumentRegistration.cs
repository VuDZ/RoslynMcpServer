using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>Uses the pinned MSBuild project-file RPCs without calling the source writer in ApplyDocumentAdded.</summary>
internal sealed class WorkspaceProjectDocumentRegistration : IAsyncDisposable
{
    internal Action? BeforeSavingProjectForTests { get; set; }

    internal Action? AfterClosingBuildHostForTests { get; set; }

    private WorkspaceProjectDocumentRegistration(IAsyncDisposable host, RegistrationMethods methods, ILogger logger)
    {
        _host = host;
        _methods = methods;
        _logger = logger;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var projectFile in _projectFiles.Values.Distinct<IAsyncDisposable>(ReferenceEqualityComparer.Instance))
        {
            await CloseAsync(projectFile, "project file").ConfigureAwait(false);
        }

        await CloseAsync(_host, "BuildHost", AfterClosingBuildHostForTests).ConfigureAwait(false);
    }

    internal static async Task<WorkspaceProjectDocumentRegistration?> OpenAsync(
        MSBuildWorkspace workspace,
        IEnumerable<Project> projects,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var projectList = projects.DistinctBy(project => project.Id).ToArray();
        if (projectList.Length == 0)
        {
            return null;
        }

        var methods = _registrationMethods
            ?? throw new NotSupportedException("This Roslyn version cannot register an MSBuild document separately from its text.");
        var languages = ImmutableArray.Create(LanguageNames.CSharp, LanguageNames.VisualBasic);
        var host = (IAsyncDisposable)methods.CreateHost.Invoke([languages, workspace.Properties, null, null, null]);
        var registration = new WorkspaceProjectDocumentRegistration(host, methods, logger);
        try
        {
            var paths = new Dictionary<string, IAsyncDisposable>(InputPathCanon.Comparer);
            foreach (var project in projectList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var projectPath = project.FilePath!;
                if (!paths.TryGetValue(projectPath, out var projectFile))
                {
                    var remoteHost = await InvokeResultAsync(methods.GetHost, host, [projectPath, cancellationToken])
                        .ConfigureAwait(false);
                    projectFile = (IAsyncDisposable)await InvokeResultAsync(
                            methods.LoadProject,
                            remoteHost,
                            [projectPath, project.Language, cancellationToken])
                        .ConfigureAwait(false);
                    paths.Add(projectPath, projectFile);
                }

                registration._projectFiles.Add(project.Id, projectFile);
            }

            return registration;
        }
        catch (Exception error)
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            if (error is OperationCanceledException)
            {
                throw;
            }

            throw new IOException($"Could not open the MSBuild project registrar: {error.GetBaseException().Message}", error);
        }
    }

    internal async Task AddDocumentAsync(ProjectId projectId, string path, CancellationToken cancellationToken)
    {
        var project = _projectFiles[projectId];
        try
        {
            await ((Task)_methods.AddDocument.Invoke(project, [path, null, cancellationToken])!).ConfigureAwait(false);
            BeforeSavingProjectForTests?.Invoke();
            await ((Task)_methods.Save.Invoke(project, [cancellationToken])!).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new IOException($"MSBuild could not register the saved document {path}: {error.GetBaseException().Message}", error);
        }
    }

    private async ValueTask CloseAsync(IAsyncDisposable resource, string name, Action? afterClosing = null)
    {
        try
        {
            await resource.DisposeAsync().ConfigureAwait(false);
            afterClosing?.Invoke();
        }
        catch (Exception error)
        {
            // Cleanup is independent of the source and project writes that already completed.
            _logger.LogWarning(error, "Could not close the MSBuild document registrar {Resource}.", name);
        }
    }

    private static async Task<object> InvokeResultAsync(MethodInfo method, object target, object?[] arguments)
    {
        var task = (Task)method.Invoke(target, arguments)!;
        await task.ConfigureAwait(false);
        return method.ReturnType.GetProperty("Result")!.GetValue(task)!;
    }

    private static RegistrationMethods? ResolveMethods()
    {
        var assembly = typeof(MSBuildWorkspace).Assembly;
        const string Prefix = "Microsoft.CodeAnalysis.MSBuild.";
        var managerType = assembly.GetType(Prefix + "BuildHostProcessManager");
        var hostType = assembly.GetType(Prefix + "RemoteBuildHost");
        var projectType = assembly.GetType(Prefix + "RemoteProjectFile");
        var binLogType = assembly.GetType(Prefix + "IBinLogPathProvider");
        if (managerType is null || hostType is null || projectType is null || binLogType is null
            || !typeof(IAsyncDisposable).IsAssignableFrom(managerType)
            || !typeof(IAsyncDisposable).IsAssignableFrom(projectType))
        {
            return null;
        }

        var createHost = managerType.GetConstructor(
            [typeof(ImmutableArray<string>), typeof(ImmutableDictionary<string, string>), binLogType, typeof(int?), typeof(ILoggerFactory)]);
        var getHost = managerType.GetMethod("GetBuildHostWithFallbackAsync", [typeof(string), typeof(CancellationToken)]);
        var loadProject = hostType.GetMethod("LoadProjectFileAsync", [typeof(string), typeof(string), typeof(CancellationToken)]);
        var addDocument = projectType.GetMethod("AddDocumentAsync", [typeof(string), typeof(string), typeof(CancellationToken)]);
        var save = projectType.GetMethod("SaveAsync", [typeof(CancellationToken)]);
        if (createHost is null || getHost is null || loadProject is null || addDocument is null || save is null
            || getHost.ReturnType != typeof(Task<>).MakeGenericType(hostType)
            || loadProject.ReturnType != typeof(Task<>).MakeGenericType(projectType)
            || addDocument.ReturnType != typeof(Task) || save.ReturnType != typeof(Task))
        {
            return null;
        }

        return new RegistrationMethods(createHost, getHost, loadProject, addDocument, save);
    }

    private sealed record RegistrationMethods(
        ConstructorInfo CreateHost,
        MethodInfo GetHost,
        MethodInfo LoadProject,
        MethodInfo AddDocument,
        MethodInfo Save);

    private readonly IAsyncDisposable _host;
    private readonly RegistrationMethods _methods;
    private readonly ILogger _logger;
    private readonly Dictionary<ProjectId, IAsyncDisposable> _projectFiles = [];
    private static readonly RegistrationMethods? _registrationMethods = ResolveMethods();
}
