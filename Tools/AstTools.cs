using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class AstTools
{
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<AstTools> _logger;

    public AstTools(SolutionManager solutionManager, ILogger<AstTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "add_using", Title = "Add using directive")]
    [Description("Adds a using directive via Roslyn AST. Writes the file. Requires load_workspace.")]
    public Task<string> AddUsing(
        [Description("Path to the .cs file.")]
        string filePath,
        [Description("Namespace to import.")]
        string namespaceName,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(nameof(AddUsing), filePath,
            doc => AstModificationHelper.AddUsingAsync(doc, namespaceName, cancellationToken),
            $"Added using `{AstModificationHelper.NormalizeNamespaceForDisplay(namespaceName)}`.",
            cancellationToken);

    [McpServerTool(Name = "remove_using", Title = "Remove using directive")]
    [Description("Removes a using directive via Roslyn AST. Writes the file. Requires load_workspace.")]
    public Task<string> RemoveUsing(
        [Description("Path to the .cs file.")]
        string filePath,
        [Description("Namespace of the using to remove.")]
        string namespaceName,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(nameof(RemoveUsing), filePath,
            doc => AstModificationHelper.RemoveUsingAsync(doc, namespaceName, cancellationToken),
            $"Removed using `{AstModificationHelper.NormalizeNamespaceForDisplay(namespaceName)}`.",
            cancellationToken);

    [McpServerTool(Name = "organize_usings", Title = "Organize usings")]
    [Description("Sorts usings and optionally removes unused ones. Writes the file. Requires load_workspace.")]
    public Task<string> OrganizeUsings(
        [Description("Path to the .cs file.")]
        string filePath,
        [Description("When true (default), remove unused usings.")]
        bool removeUnused = true,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(nameof(OrganizeUsings), filePath,
            doc => AstModificationHelper.OrganizeUsingsAsync(doc, removeUnused, cancellationToken),
            "Organized usings.",
            cancellationToken);

    [McpServerTool(Name = "add_method_to_class", Title = "Add method to class")]
    [Description("Inserts a parsed method into a class. Writes the file. Requires load_workspace.")]
    public Task<string> AddMethodToClass(
        [Description("Path to the .cs file.")]
        string filePath,
        [Description("Target class name.")]
        string className,
        [Description("Full method declaration source.")]
        string methodSource,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(nameof(AddMethodToClass), filePath,
            doc => AstModificationHelper.AddMethodToClassAsync(doc, className, methodSource, cancellationToken),
            $"Added method to class `{className}`.",
            cancellationToken);

    [McpServerTool(Name = "update_method_body", Title = "Update method body")]
    [Description("Replaces a method body via Roslyn AST. Writes the file. Requires load_workspace. Prefer over apply_patch for body-only edits.")]
    public Task<string> UpdateMethodBody(
        [Description("Path to the .cs file.")]
        string filePath,
        [Description("Class containing the method.")]
        string className,
        [Description("Method name to update.")]
        string methodName,
        [Description("New body: statements only, or a full { ... } block.")]
        string newBody,
        [Description("Parameter type names to disambiguate overloads. Required when overloads exist.")]
        string[]? parameterTypes = null,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(nameof(UpdateMethodBody), filePath,
            doc => AstModificationHelper.UpdateMethodBodyAsync(
                doc, className, methodName, newBody, parameterTypes, cancellationToken),
            $"Updated body of `{className}.{methodName}`.",
            cancellationToken);

    [McpServerTool(Name = "add_property_to_class", Title = "Add property to class")]
    [Description("Inserts a parsed property into a class. Writes the file. Requires load_workspace.")]
    public Task<string> AddPropertyToClass(
        [Description("Path to the .cs file.")]
        string filePath,
        [Description("Target class name.")]
        string className,
        [Description("Full property declaration source.")]
        string propertySource,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(nameof(AddPropertyToClass), filePath,
            doc => AstModificationHelper.AddPropertyToClassAsync(doc, className, propertySource, cancellationToken),
            $"Added property to class `{className}`.",
            cancellationToken);

    [McpServerTool(Name = "add_field_to_class", Title = "Add field to class")]
    [Description("Inserts a parsed field into a class. Writes the file. Requires load_workspace.")]
    public Task<string> AddFieldToClass(
        [Description("Path to the .cs file.")]
        string filePath,
        [Description("Target class name.")]
        string className,
        [Description("Full field declaration source.")]
        string fieldSource,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(nameof(AddFieldToClass), filePath,
            doc => AstModificationHelper.AddFieldToClassAsync(doc, className, fieldSource, cancellationToken),
            $"Added field to class `{className}`.",
            cancellationToken);

    [McpServerTool(Name = "remove_member", Title = "Remove class member")]
    [Description("Removes a method, property, field, or event by name. Writes the file. Requires load_workspace.")]
    public Task<string> RemoveMember(
        [Description("Path to the .cs file.")]
        string filePath,
        [Description("Class containing the member.")]
        string className,
        [Description("Member name to remove.")]
        string memberName,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(nameof(RemoveMember), filePath,
            doc => AstModificationHelper.RemoveMemberAsync(doc, className, memberName, cancellationToken),
            $"Removed member `{memberName}` from class `{className}`.",
            cancellationToken);

    [McpServerTool(Name = "add_type_to_class_bases", Title = "Add base type or interface")]
    [Description("Adds a base class or interface to a class. Writes the file. Requires load_workspace.")]
    public Task<string> AddTypeToClassBases(
        [Description("Path to the .cs file.")]
        string filePath,
        [Description("Target class name.")]
        string className,
        [Description("Base class or interface type name to add.")]
        string typeName,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(nameof(AddTypeToClassBases), filePath,
            doc => InterfaceImplementationHelper.AddTypeToClassBasesAsync(doc, className, typeName, cancellationToken),
            $"Added `{typeName}` to base list of `{className}`.",
            cancellationToken);

    [McpServerTool(Name = "implement_interface", Title = "Implement interface stubs")]
    [Description("Adds an interface and NotImplemented stubs for missing members. Writes the file. Requires load_workspace.")]
    public Task<string> ImplementInterface(
        [Description("Path to the .cs file.")]
        string filePath,
        [Description("Target class name.")]
        string className,
        [Description("Interface name to implement.")]
        string interfaceName,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(nameof(ImplementInterface), filePath,
            doc => InterfaceImplementationHelper.ImplementInterfaceAsync(doc, className, interfaceName, cancellationToken),
            $"Implemented interface `{interfaceName}` on class `{className}`.",
            cancellationToken);

    private async Task<string> ApplyAsync(
        string toolName,
        string filePath,
        Func<Microsoft.CodeAnalysis.Document, Task<Microsoft.CodeAnalysis.Document>> mutate,
        string successMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var (document, baseSolution) = await ResolveDocumentAsync(filePath, cancellationToken);
            var newDocument = await mutate(document).ConfigureAwait(false);
            var write = await _solutionManager.ApplySolutionChangesToDiskAsync(
                baseSolution, newDocument.Project.Solution, cancellationToken).ConfigureAwait(false);
            if (!write.IsFullSuccess)
            {
                return ToolTelemetry.TraceAndReturn(toolName, write.FormatAdapterMessage(successMessage));
            }

            return ToolTelemetry.TraceAndReturn(
                toolName,
                $"{successMessage} File: `{_solutionManager.ResolvePathAgainstWorkspace(filePath)}`. Files touched: {write.SavedPaths.Count}.");
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, $"`{toolName}` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{ToolName} failed for {FilePath}", toolName, filePath);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed: {ex.Message}");
        }
    }

    private async Task<(Microsoft.CodeAnalysis.Document Document, Microsoft.CodeAnalysis.Solution BaseSolution)> ResolveDocumentAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("filePath is empty.");
        }

        var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path must point to a .cs file: `{fullPath}`.");
        }

        var document = await _solutionManager.FindDocumentAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            throw new InvalidOperationException(
                $"Document not found in workspace: `{fullPath}`. Call `load_workspace` first.");
        }

        return (document, document.Project.Solution);
    }
}
