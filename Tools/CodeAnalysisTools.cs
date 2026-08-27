using System.ComponentModel;
using System.Text;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using DecompilerAccessibility = ICSharpCode.Decompiler.TypeSystem.Accessibility;
using DecompilerTypeKind = ICSharpCode.Decompiler.TypeSystem.TypeKind;

namespace RoslynMcpServer.Tools;

public sealed class CodeAnalysisTools
{
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<CodeAnalysisTools> _logger;

    public CodeAnalysisTools(SolutionManager solutionManager, ILogger<CodeAnalysisTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "get_class_skeleton", Title = "Get C# class skeleton")]
    [Description(
        "Extracts the high-level skeleton (contract) of a C# file from the **loaded Roslyn workspace** "
        + "(applies **saved** `.cs` from disk first; unsaved editor buffers are ignored). "
        + "Returns namespaces, types, properties, and method signatures; method bodies omitted. "
        + "Requires `load_workspace` and a document in that workspace. For raw disk with no workspace (or to skip the index) use `get_code_skeleton`; for NuGet/DLL types use `get_decompiled_class_skeleton`.")]
    public async Task<string> GetClassSkeleton(
        [Description("Path to a .cs file in the loaded workspace (same argument name `filePath` as get_file_content).")]
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
        var document = await _solutionManager.FindDocumentAsync(fullPath, cancellationToken);
        if (document is null)
        {
            return ToolTelemetry.TraceAndReturn(
                nameof(GetClassSkeleton),
                $"The file was not found in the workspace: `{fullPath}`. Load the solution or load_workspace first, or verify the path.");
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
        if (semanticModel is null || syntaxRoot is null)
        {
            return ToolTelemetry.TraceAndReturn(
                nameof(GetClassSkeleton),
                $"Could not obtain semantic model or syntax tree for file: `{fullPath}`.");
        }

        var walker = new ClassSkeletonWalker();
        walker.Visit(syntaxRoot);

        var skeleton = walker.GetText();
        if (string.IsNullOrWhiteSpace(skeleton))
        {
            skeleton = "// No namespace or type declarations found.";
        }

        return ToolTelemetry.TraceAndReturn(
            nameof(GetClassSkeleton),
            $"```csharp{Environment.NewLine}{skeleton}{Environment.NewLine}```");
    }

    [McpServerTool(Name = "get_diagnostics_for_file", Title = "Get diagnostics for file")]
    [Description(
        "Returns Roslyn compiler diagnostics (Warning and Error) for a single C# file from the active workspace. "
        + "Applies **saved** `.cs` from disk first; unsaved editor buffers are ignored.")]
    public async Task<string> GetDiagnosticsForFile(
        [Description("Absolute or workspace-relative path to the target .cs file (same parameter name as get_file_content / find_symbol_references).")] string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(GetDiagnosticsForFile), "Error: `filePath` is empty.");
            }

            var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            if (!string.Equals(Path.GetExtension(fullPath), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                return ToolTelemetry.TraceAndReturn(nameof(GetDiagnosticsForFile), $"Path must point to a .cs file: `{fullPath}`.");
            }

            var document = await _solutionManager.FindDocumentAsync(fullPath, cancellationToken);
            if (document is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(GetDiagnosticsForFile),
                    $"Document was not found in the active workspace: `{fullPath}`.");
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(GetDiagnosticsForFile),
                    $"Could not obtain semantic model for `{fullPath}`.");
            }

            var diagnostics = semanticModel.GetDiagnostics()
                .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                .ToList();

            if (diagnostics.Count == 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(GetDiagnosticsForFile),
                    $"No compiler errors or warnings found for `{fullPath}`.");
            }

            var lines = diagnostics
                .Select(d =>
                {
                    var start = d.Location.GetLineSpan().StartLinePosition;
                    return $"[{d.Severity}] Line {start.Line + 1}, Col {start.Character + 1}: ({d.Id}) {d.GetMessage()}";
                })
                .ToList();

            var header = $"Diagnostics for `{fullPath}` ({diagnostics.Count}). Use get_code_fixes with diagnosticId, line, and column.";
            return ToolTelemetry.TraceAndReturn(
                nameof(GetDiagnosticsForFile),
                header + Environment.NewLine + string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDiagnosticsForFile failed for {FilePath}", filePath);
            return ToolTelemetry.TraceAndReturn(nameof(GetDiagnosticsForFile), $"Failed to get diagnostics: {ex.Message}");
        }
    }

    [McpServerTool(Name = "explore_assembly", Title = "Explore referenced assembly")]
    [Description(
        "Opens an external assembly (NuGet or other DLL) with ILSpy and returns namespaces with visible top-level types. "
        + "Provide `assemblyName` (no `.dll`) or `assemblyPath` (absolute path to `.dll`). "
        + "`assemblyName` resolves exactly as `{name}.dll` via workspace MetadataReferences → project `deps.json` → NuGet cache (no fuzzy match). "
        + "Call `load_workspace` when using `assemblyName` only.")]
    public Task<string> ExploreAssembly(
        [Description("Assembly simple name without `.dll` (e.g. `Microsoft.TeamFoundation.Client`). Exact `{name}.dll` via MetadataReferences → deps.json → NuGet.")] string? assemblyName = null,
        [Description("Absolute path to a `.dll` file. Bypasses workspace resolve.")] string? assemblyPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var solution = _solutionManager.GetCurrentSolution();
            var resolved = AssemblyReferenceResolver.Resolve(solution, assemblyName, assemblyPath);
            if (!resolved.Success)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ExploreAssembly), resolved.ErrorMessage!));
            }

            var dllPath = resolved.DllPath!;
            cancellationToken.ThrowIfCancellationRequested();
            var decompiler = DecompilerHost.Create(dllPath);

            var visibleTopLevelTypes = decompiler.TypeSystem.MainModule.TopLevelTypeDefinitions
                .Where(static typeDef => IsVisibleClassOrInterface(typeDef))
                .OrderBy(typeDef => typeDef.Namespace, StringComparer.Ordinal)
                .ThenBy(typeDef => typeDef.Name, StringComparer.Ordinal)
                .ToList();

            if (visibleTopLevelTypes.Count == 0)
            {
                return Task.FromResult(
                    ToolTelemetry.TraceAndReturn(
                        nameof(ExploreAssembly),
                        $"Assembly `{Path.GetFileName(dllPath)}` was found, but no public/protected top-level classes or interfaces were discovered."));
            }

            var byNamespace = visibleTopLevelTypes
                .GroupBy(typeDef => string.IsNullOrWhiteSpace(typeDef.Namespace) ? "(global namespace)" : typeDef.Namespace)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Assembly: `{Path.GetFileName(dllPath)}`");
            sb.AppendLine($"Path: `{dllPath}`");
            sb.AppendLine($"Namespaces: {byNamespace.Count}, Types: {visibleTopLevelTypes.Count}");
            sb.AppendLine();

            foreach (var group in byNamespace)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine($"## {group.Key}");
                foreach (var typeDef in group)
                {
                    var kind = typeDef.Kind == DecompilerTypeKind.Interface ? "interface" : "class";
                    sb.AppendLine($"- {kind} {typeDef.Name}");
                }

                sb.AppendLine();
            }

            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ExploreAssembly), sb.ToString().TrimEnd()));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ExploreAssembly), "ExploreAssembly was cancelled."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExploreAssembly failed for {AssemblyName} {AssemblyPath}", assemblyName, assemblyPath);
            return Task.FromResult(
                ToolTelemetry.TraceAndReturn(
                    nameof(ExploreAssembly),
                    FormatDecompilerToolError(assemblyName, assemblyPath, "Failed to explore assembly", ex)));
        }
    }

    [McpServerTool(Name = "decompile_type", Title = "Decompile referenced type")]
    [Description(
        "Decompiles a specific type from an external assembly into C# via ILSpy. "
        + "Resolve `assemblyName` (exact `{name}.dll`) via MetadataReferences → `deps.json` → NuGet cache, or pass `assemblyPath`. "
        + "Then finds `fullTypeName` and returns decompiled source. Circuit breaker: ~500 lines — for large types use `get_decompiled_class_skeleton` / `get_decompiled_method_body`. "
        + "Call `load_workspace` when using `assemblyName` only.")]
    public Task<string> DecompileType(
        [Description("Assembly simple name without `.dll`, or omit when `assemblyPath` is set. Exact resolve: MetadataReferences → deps.json → NuGet.")] string? assemblyName = null,
        [Description("Absolute path to a `.dll` file. Bypasses workspace resolve.")] string? assemblyPath = null,
        [Description("Full type name with namespace (for example: `Microsoft.AspNetCore.Mvc.ControllerBase`).")] string fullTypeName = "",
        CancellationToken cancellationToken = default)
    {
        try
        {
            const int maxAllowedLines = 500;

            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(DecompileType), "Error: `fullTypeName` is empty."));
            }

            var solution = _solutionManager.GetCurrentSolution();
            var resolved = AssemblyReferenceResolver.Resolve(solution, assemblyName, assemblyPath);
            if (!resolved.Success)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(DecompileType), resolved.ErrorMessage!));
            }

            var dllPath = resolved.DllPath!;
            cancellationToken.ThrowIfCancellationRequested();
            var decompiler = DecompilerHost.Create(dllPath);
            var typeInfo = decompiler.TypeSystem.MainModule.Compilation.FindType(new FullTypeName(fullTypeName));
            var typeDefinition = typeInfo?.GetDefinition();
            if (typeDefinition is null)
            {
                return Task.FromResult(
                    ToolTelemetry.TraceAndReturn(
                        nameof(DecompileType),
                        $"Type `{fullTypeName}` was not found in assembly `{Path.GetFileName(dllPath)}`."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var code = decompiler.DecompileTypeAsString(typeDefinition.FullTypeName);
            if (string.IsNullOrWhiteSpace(code))
            {
                return Task.FromResult(
                    ToolTelemetry.TraceAndReturn(
                        nameof(DecompileType),
                        $"Type `{fullTypeName}` was resolved but decompilation returned empty output."));
            }

            var lineCount = 1;
            foreach (var ch in code)
            {
                if (ch == '\n')
                {
                    lineCount++;
                }
            }

            if (lineCount > maxAllowedLines)
            {
                return Task.FromResult(
                    ToolTelemetry.TraceAndReturn(
                        nameof(DecompileType),
                        $"Error: The requested type is too large ({lineCount} lines) to be fully decompiled into the context window. Please use `GetDecompiledClassSkeleton` to view its structure, and `GetDecompiledMethodBody` to inspect specific methods."));
            }

            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(DecompileType), code.TrimEnd()));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(DecompileType), "DecompileType was cancelled."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DecompileType failed for {AssemblyName}::{FullTypeName}", assemblyName, fullTypeName);
            return Task.FromResult(
                ToolTelemetry.TraceAndReturn(
                    nameof(DecompileType),
                    FormatDecompilerToolError(assemblyName, assemblyPath, $"Failed to decompile type `{fullTypeName}`", ex)));
        }
    }

    [McpServerTool(Name = "get_decompiled_class_skeleton", Title = "Get decompiled type skeleton")]
    [Description(
        "Signatures-only C# skeleton for a type in an external assembly (no method bodies). "
        + "Resolve `assemblyName` exactly via MetadataReferences → `deps.json` → NuGet, or pass `assemblyPath`. "
        + "Prefer this over `decompile_type` for large types. Call `load_workspace` when using `assemblyName` only.")]
    public Task<string> GetDecompiledClassSkeleton(
        [Description("Assembly simple name without `.dll`, or omit when `assemblyPath` is set. Exact resolve: MetadataReferences → deps.json → NuGet.")] string? assemblyName = null,
        [Description("Absolute path to a `.dll` file. Bypasses workspace resolve.")] string? assemblyPath = null,
        [Description("Full type name with namespace (for example: `Microsoft.AspNetCore.Mvc.ControllerBase`).")] string fullTypeName = "",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetDecompiledClassSkeleton), "Error: `fullTypeName` is empty."));
            }

            var solution = _solutionManager.GetCurrentSolution();
            var resolved = AssemblyReferenceResolver.Resolve(solution, assemblyName, assemblyPath);
            if (!resolved.Success)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetDecompiledClassSkeleton), resolved.ErrorMessage!));
            }

            var dllPath = resolved.DllPath!;
            cancellationToken.ThrowIfCancellationRequested();
            var decompiler = DecompilerHost.Create(dllPath);
            var typeInfo = decompiler.TypeSystem.MainModule.Compilation.FindType(new FullTypeName(fullTypeName));
            var typeDefinition = typeInfo?.GetDefinition();
            if (typeDefinition is null)
            {
                return Task.FromResult(
                    ToolTelemetry.TraceAndReturn(
                        nameof(GetDecompiledClassSkeleton),
                        $"Type `{fullTypeName}` was not found in assembly `{Path.GetFileName(dllPath)}`."));
            }

            var typeName = BuildTypeName(typeDefinition.Name, typeDefinition.TypeParameters.Select(tp => tp.Name));
            var namespaceName = string.IsNullOrWhiteSpace(typeDefinition.Namespace) ? null : typeDefinition.Namespace;
            var typeAccessibility = GetAccessibilityKeyword(typeDefinition.Accessibility);
            var typeKind = typeDefinition.Kind == DecompilerTypeKind.Interface ? "interface" : "class";

            var fields = typeDefinition.Fields
                .Where(static field => IsPublicOrProtected(field.Accessibility) && !IsCompilerGeneratedName(field.Name))
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .ToList();
            var properties = typeDefinition.Properties
                .Where(static property => IsPublicOrProtected(property.Accessibility) && !IsCompilerGeneratedName(property.Name))
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToList();
            var methods = typeDefinition.Methods
                .Where(static method =>
                    IsPublicOrProtected(method.Accessibility)
                    && !method.IsAccessor
                    && !method.IsOperator
                    && !IsCompilerGeneratedName(method.Name))
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .ThenBy(method => method.Parameters.Count)
                .ToList();

            var sb = new StringBuilder();
            if (namespaceName is not null)
            {
                sb.Append("namespace ").Append(namespaceName).AppendLine(";");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(typeAccessibility))
            {
                sb.Append(typeAccessibility).Append(' ');
            }

            sb.Append(typeKind).Append(' ').Append(typeName).AppendLine();
            sb.AppendLine("{");

            foreach (var field in fields)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.Append("    ")
                    .Append(FormatModifiers(field.Accessibility, field.IsStatic))
                    .Append(FormatTypeName(field.Type))
                    .Append(' ')
                    .Append(field.Name)
                    .AppendLine(";");
            }

            foreach (var property in properties)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var getter = property.Getter is null ? string.Empty : "get; ";
                var setter = property.Setter is null ? string.Empty : "set; ";
                sb.Append("    ")
                    .Append(FormatModifiers(property.Accessibility, property.IsStatic))
                    .Append(FormatTypeName(property.ReturnType))
                    .Append(' ')
                    .Append(property.Name)
                    .Append(" { ")
                    .Append(getter)
                    .Append(setter)
                    .AppendLine("}");
            }

            foreach (var method in methods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var methodName = method.IsConstructor ? typeDefinition.Name : method.Name;
                var typeParameters = method.TypeParameters.Count == 0
                    ? string.Empty
                    : $"<{string.Join(", ", method.TypeParameters.Select(tp => tp.Name))}>";
                var returnType = method.IsConstructor ? string.Empty : $"{FormatTypeName(method.ReturnType)} ";
                var parameters = string.Join(
                    ", ",
                    method.Parameters.Select(
                        static p => $"{FormatTypeName(p.Type)} {(string.IsNullOrWhiteSpace(p.Name) ? "arg" : p.Name)}"));

                sb.Append("    ")
                    .Append(FormatModifiers(method.Accessibility, method.IsStatic))
                    .Append(returnType)
                    .Append(methodName)
                    .Append(typeParameters)
                    .Append('(')
                    .Append(parameters)
                    .AppendLine(");");
            }

            if (fields.Count == 0 && properties.Count == 0 && methods.Count == 0)
            {
                sb.AppendLine("    // No public/protected fields, properties, or methods.");
            }

            sb.AppendLine("}");
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetDecompiledClassSkeleton), sb.ToString().TrimEnd()));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetDecompiledClassSkeleton), "GetDecompiledClassSkeleton was cancelled."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDecompiledClassSkeleton failed for {AssemblyName}::{FullTypeName}", assemblyName, fullTypeName);
            return Task.FromResult(
                ToolTelemetry.TraceAndReturn(
                    nameof(GetDecompiledClassSkeleton),
                    FormatDecompilerToolError(assemblyName, assemblyPath, $"Failed to build skeleton for `{fullTypeName}`", ex)));
        }
    }

    [McpServerTool(Name = "get_decompiled_method_body", Title = "Get decompiled method body")]
    [Description(
        "Decompiles only method member(s) from a type in an external assembly. "
        + "Resolve `assemblyName` exactly via MetadataReferences → `deps.json` → NuGet, or pass `assemblyPath`. "
        + "Matches all overloads by `methodName`. Prefer over full `decompile_type` for focused inspection.")]
    public Task<string> GetDecompiledMethodBody(
        [Description("Assembly simple name without `.dll`, or omit when `assemblyPath` is set. Exact resolve: MetadataReferences → deps.json → NuGet.")] string? assemblyName = null,
        [Description("Absolute path to a `.dll` file. Bypasses workspace resolve.")] string? assemblyPath = null,
        [Description("Full type name with namespace (for example: `Microsoft.AspNetCore.Mvc.ControllerBase`).")] string fullTypeName = "",
        [Description("Method name to decompile. All overloads with this name are returned.")] string methodName = "",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetDecompiledMethodBody), "Error: `fullTypeName` is empty."));
            }

            if (string.IsNullOrWhiteSpace(methodName))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetDecompiledMethodBody), "Error: `methodName` is empty."));
            }

            var solution = _solutionManager.GetCurrentSolution();
            var resolved = AssemblyReferenceResolver.Resolve(solution, assemblyName, assemblyPath);
            if (!resolved.Success)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetDecompiledMethodBody), resolved.ErrorMessage!));
            }

            var dllPath = resolved.DllPath!;
            cancellationToken.ThrowIfCancellationRequested();
            var decompiler = DecompilerHost.Create(dllPath);
            var typeInfo = decompiler.TypeSystem.MainModule.Compilation.FindType(new FullTypeName(fullTypeName));
            var typeDefinition = typeInfo?.GetDefinition();
            if (typeDefinition is null)
            {
                return Task.FromResult(
                    ToolTelemetry.TraceAndReturn(
                        nameof(GetDecompiledMethodBody),
                        $"Type `{fullTypeName}` was not found in assembly `{Path.GetFileName(dllPath)}`."));
            }

            var targetMethodName = methodName.Trim();
            var matches = typeDefinition.Methods
                .Where(method =>
                    !method.IsAccessor
                    && string.Equals(method.Name, targetMethodName, StringComparison.Ordinal))
                .OrderBy(method => method.Parameters.Count)
                .ToList();

            if (matches.Count == 0)
            {
                return Task.FromResult(
                    ToolTelemetry.TraceAndReturn(
                        nameof(GetDecompiledMethodBody),
                        $"Method `{targetMethodName}` was not found in type `{fullTypeName}`."));
            }

            var chunks = new List<string>(matches.Count);
            foreach (var method in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snippet = decompiler.DecompileAsString(method.MetadataToken);
                if (string.IsNullOrWhiteSpace(snippet))
                {
                    continue;
                }

                chunks.Add(snippet.TrimEnd());
            }

            if (chunks.Count == 0)
            {
                return Task.FromResult(
                    ToolTelemetry.TraceAndReturn(
                        nameof(GetDecompiledMethodBody),
                        $"Method `{targetMethodName}` was found ({matches.Count} overload(s)), but decompilation returned empty output."));
            }

            var payload = string.Join($"{Environment.NewLine}{Environment.NewLine}---{Environment.NewLine}{Environment.NewLine}", chunks);
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetDecompiledMethodBody), payload));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetDecompiledMethodBody), "GetDecompiledMethodBody was cancelled."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDecompiledMethodBody failed for {AssemblyName}::{FullTypeName}.{MethodName}", assemblyName, fullTypeName, methodName);
            return Task.FromResult(
                ToolTelemetry.TraceAndReturn(
                    nameof(GetDecompiledMethodBody),
                    FormatDecompilerToolError(
                        assemblyName,
                        assemblyPath,
                        $"Failed to decompile method `{methodName}` from `{fullTypeName}`",
                        ex)));
        }
    }

    private static string FormatDecompilerToolError(
        string? assemblyName,
        string? assemblyPath,
        string preamble,
        Exception ex)
    {
        var dllHint = !string.IsNullOrWhiteSpace(assemblyPath)
            ? Path.GetFullPath(assemblyPath.Trim())
            : assemblyName;
        var detail = !string.IsNullOrWhiteSpace(assemblyPath)
            ? DecompilerHost.FormatResolveError(ex, Path.GetFullPath(assemblyPath.Trim()), null)
            : ex.Message;
        return $"{preamble} from `{dllHint}`: {detail}";
    }

    private static bool IsCompilerGeneratedName(string value)
    {
        return value.Contains('<', StringComparison.Ordinal) || value.Contains('>', StringComparison.Ordinal);
    }

    private static string BuildTypeName(string name, IEnumerable<string> typeParameterNames)
    {
        var cleanedName = name;
        var genericTickIndex = cleanedName.IndexOf('`', StringComparison.Ordinal);
        if (genericTickIndex >= 0)
        {
            cleanedName = cleanedName[..genericTickIndex];
        }

        var parameters = typeParameterNames.ToList();
        if (parameters.Count == 0)
        {
            return cleanedName;
        }

        return $"{cleanedName}<{string.Join(", ", parameters)}>";
    }

    private static string FormatTypeName(IType type)
    {
        var value = type.ToString() ?? string.Empty;
        return value.Replace("/", ".", StringComparison.Ordinal);
    }

    private static string FormatModifiers(DecompilerAccessibility accessibility, bool isStatic)
    {
        var access = GetAccessibilityKeyword(accessibility);
        if (isStatic)
        {
            return string.IsNullOrWhiteSpace(access) ? "static " : $"{access} static ";
        }

        return string.IsNullOrWhiteSpace(access) ? string.Empty : $"{access} ";
    }

    private static string GetAccessibilityKeyword(DecompilerAccessibility accessibility)
    {
        return accessibility switch
        {
            DecompilerAccessibility.Public => "public",
            DecompilerAccessibility.Protected => "protected",
            DecompilerAccessibility.ProtectedAndInternal => "protected internal",
            DecompilerAccessibility.ProtectedOrInternal => "protected internal",
            _ => string.Empty
        };
    }

    private static bool IsVisibleClassOrInterface(ITypeDefinition typeDef)
    {
        if (typeDef.Name.Contains('<', StringComparison.Ordinal) || typeDef.Name.Contains('>', StringComparison.Ordinal))
        {
            return false;
        }

        if (typeDef.Kind is not (DecompilerTypeKind.Class or DecompilerTypeKind.Interface))
        {
            return false;
        }

        return IsPublicOrProtected(typeDef.Accessibility);
    }

    private static bool IsPublicOrProtected(DecompilerAccessibility accessibility)
    {
        return accessibility == DecompilerAccessibility.Public
            || accessibility == DecompilerAccessibility.Protected
            || accessibility == DecompilerAccessibility.ProtectedAndInternal
            || accessibility == DecompilerAccessibility.ProtectedOrInternal;
    }

    private sealed class ClassSkeletonWalker : CSharpSyntaxWalker
    {
        private readonly StringBuilder _builder = new();
        private int _indentLevel;

        public string GetText() => _builder.ToString().TrimEnd();

        public override void VisitCompilationUnit(CompilationUnitSyntax node)
        {
            foreach (var attributeList in node.AttributeLists)
            {
                WriteLine(attributeList.ToString().Trim());
            }

            foreach (var usingDirective in node.Usings)
            {
                WriteLine(usingDirective.ToString().Trim());
            }

            if (node.AttributeLists.Count > 0 || node.Usings.Count > 0)
            {
                WriteLine();
            }

            foreach (var member in node.Members)
            {
                Visit(member);
            }
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            WriteLine($"namespace {node.Name}");
            WriteLine("{");
            _indentLevel++;
            foreach (var member in node.Members)
            {
                Visit(member);
            }

            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            WriteLine($"namespace {node.Name};");
            WriteLine();
            foreach (var member in node.Members)
            {
                Visit(member);
            }
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            WriteLine(BuildTypeHeader(node.AttributeLists, node.Modifiers, "class", node.Identifier.Text, node.TypeParameterList, node.BaseList, node.ConstraintClauses));
            WriteLine("{");
            _indentLevel++;
            WriteMemberSkeletons(node.Members);
            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            WriteLine(BuildTypeHeader(node.AttributeLists, node.Modifiers, "interface", node.Identifier.Text, node.TypeParameterList, node.BaseList, node.ConstraintClauses));
            WriteLine("{");
            _indentLevel++;
            WriteMemberSkeletons(node.Members);
            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            WriteLine(BuildTypeHeader(node.AttributeLists, node.Modifiers, "struct", node.Identifier.Text, node.TypeParameterList, node.BaseList, node.ConstraintClauses));
            WriteLine("{");
            _indentLevel++;
            WriteMemberSkeletons(node.Members);
            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            WriteLine(BuildRecordHeader(node, GetRecordKindLabel(node)));
            WriteLine("{");
            _indentLevel++;
            WriteMemberSkeletons(node.Members);
            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        private void WriteMemberSkeletons(SyntaxList<MemberDeclarationSyntax> members)
        {
            foreach (var member in members)
            {
                switch (member)
                {
                    case FieldDeclarationSyntax field:
                        WriteLine(ToSingleLine(field));
                        break;
                    case PropertyDeclarationSyntax property:
                        WriteLine(ToSingleLine(SanitizeProperty(property)));
                        break;
                    case MethodDeclarationSyntax method:
                        WriteLine(ToSingleLine(SanitizeMethod(method)));
                        break;
                    case ConstructorDeclarationSyntax ctor:
                        WriteLine(ToSingleLine(SanitizeConstructor(ctor)));
                        break;
                    case ClassDeclarationSyntax nestedClass:
                        VisitClassDeclaration(nestedClass);
                        break;
                    case InterfaceDeclarationSyntax nestedInterface:
                        VisitInterfaceDeclaration(nestedInterface);
                        break;
                    case StructDeclarationSyntax nestedStruct:
                        VisitStructDeclaration(nestedStruct);
                        break;
                    case RecordDeclarationSyntax nestedRecord:
                        VisitRecordDeclaration(nestedRecord);
                        break;
                }
            }
        }

        private static string GetRecordKindLabel(RecordDeclarationSyntax node)
        {
            return node.Kind() switch
            {
                SyntaxKind.RecordStructDeclaration => "record struct",
                _ => "record"
            };
        }

        private static string BuildRecordHeader(RecordDeclarationSyntax node, string keyword)
        {
            var parts = new List<string>();

            foreach (var attributeList in node.AttributeLists)
            {
                parts.Add(attributeList.ToFullString().Trim());
            }

            if (node.Modifiers.Count > 0)
            {
                parts.Add(string.Join(" ", node.Modifiers.Select(m => m.Text)));
            }

            parts.Add(keyword);
            parts.Add(node.Identifier.Text);

            if (node.TypeParameterList is not null)
            {
                parts.Add(node.TypeParameterList.ToFullString().Trim());
            }

            if (node.ParameterList is not null && node.ParameterList.Parameters.Count > 0)
            {
                parts.Add(node.ParameterList.ToFullString().Trim());
            }

            if (node.BaseList is not null)
            {
                parts.Add(node.BaseList.ToFullString().Trim());
            }

            if (node.ConstraintClauses.Count > 0)
            {
                parts.Add(string.Join(" ", node.ConstraintClauses.Select(c => c.ToFullString().Trim())));
            }

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static string BuildTypeHeader(
            SyntaxList<AttributeListSyntax> attributes,
            SyntaxTokenList modifiers,
            string keyword,
            string identifier,
            TypeParameterListSyntax? typeParameters,
            BaseListSyntax? baseList,
            SyntaxList<TypeParameterConstraintClauseSyntax> constraints)
        {
            var parts = new List<string>();

            foreach (var attributeList in attributes)
            {
                parts.Add(attributeList.ToFullString().Trim());
            }

            if (modifiers.Count > 0)
            {
                parts.Add(string.Join(" ", modifiers.Select(m => m.Text)));
            }

            parts.Add(keyword);
            parts.Add(identifier);

            if (typeParameters is not null)
            {
                parts.Add(typeParameters.ToFullString().Trim());
            }

            if (baseList is not null)
            {
                parts.Add(baseList.ToFullString().Trim());
            }

            if (constraints.Count > 0)
            {
                parts.Add(string.Join(" ", constraints.Select(c => c.ToFullString().Trim())));
            }

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static MethodDeclarationSyntax SanitizeMethod(MethodDeclarationSyntax method)
        {
            return method
                .WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        private static ConstructorDeclarationSyntax SanitizeConstructor(ConstructorDeclarationSyntax ctor)
        {
            return ctor
                .WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        private static PropertyDeclarationSyntax SanitizeProperty(PropertyDeclarationSyntax property)
        {
            if (property.ExpressionBody is not null)
            {
                return property
                    .WithExpressionBody(null)
                    .WithInitializer(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
            }

            if (property.AccessorList is null)
            {
                return property;
            }

            var sanitizedAccessors = property.AccessorList.Accessors.Select(accessor =>
                accessor
                    .WithBody(null)
                    .WithExpressionBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));

            return property.WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(sanitizedAccessors)));
        }

        private static string ToSingleLine(SyntaxNode node)
        {
            return node.NormalizeWhitespace(eol: " ", indentation: string.Empty)
                .ToFullString()
                .Trim();
        }

        private void WriteLine(string? text = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                _builder.AppendLine();
                return;
            }

            _builder.Append(' ', _indentLevel * 4);
            _builder.AppendLine(text);
        }
    }
}
