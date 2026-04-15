using System.ComponentModel;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Tools;

public sealed class CodeSkeletonTools
{
    public const int MaxDirectoryFiles = 20;

    private static readonly HashSet<string> SkippedDirectorySegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "Test",
        "Tests"
    };

    private readonly ILogger<CodeSkeletonTools> _logger;

    public CodeSkeletonTools(ILogger<CodeSkeletonTools> logger)
    {
        _logger = logger;
    }

    [McpServerTool(Name = "get_code_skeleton", Title = "Get C# code skeleton from disk")]
    [Description(
        "Parses C# source from disk (no workspace required) and returns only a **structural skeleton**: namespaces, types, fields, properties, and method/constructor **signatures**. "
        + "Method and constructor bodies are replaced with empty blocks `{ }`; expression-bodied members become block-bodied stubs; property accessors with implementations are reduced to auto-style `get;` / `set;` / `init;`. "
        + "Use this to grasp what a large `.cs` file or folder contains **without** loading full method bodies into the LLM context. "
        + "Pass a single `.cs` file path, or a directory (recursive `.cs` discovery, skipping `bin`, `obj`, `Test`, and `Tests` segments; at most 20 files).")]
    public async Task<string> GetCodeSkeleton(
        [Description("Absolute path to a `.cs` file or a directory to scan for `.cs` files.")]
        string path,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(GetCodeSkeleton);

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `path` is empty.");
            }

            var fullPath = Path.GetFullPath(path.Trim());
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Path not found: `{fullPath}`");
            }

            IReadOnlyList<string> targets;
            var truncatedDir = false;
            if (File.Exists(fullPath))
            {
                if (!string.Equals(Path.GetExtension(fullPath), ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    return ToolTelemetry.TraceAndReturn(toolName, $"File must have extension `.cs`: `{fullPath}`");
                }

                targets = [fullPath];
            }
            else
            {
                var (files, wasTruncated) = CollectCsFilesFromDirectory(fullPath, cancellationToken);
                truncatedDir = wasTruncated;
                if (files.Count == 0)
                {
                    return ToolTelemetry.TraceAndReturn(
                        toolName,
                        $"No `.cs` files found under `{fullPath}` (after skipping `bin`/`obj`/`Test`/`Tests` segments).");
                }

                targets = files;
            }

            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
            var sb = new StringBuilder();
            for (var i = 0; i < targets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = targets[i];
                var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                var tree = CSharpSyntaxTree.ParseText(text, parseOptions, path: file, cancellationToken: cancellationToken);
                var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                var rewritten = new SkeletonRewriter().Visit(root) ?? root;
                var formatted = rewritten.NormalizeWhitespace().ToFullString();

                if (i > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }

                sb.AppendLine($"## `{file}`");
                sb.AppendLine();
                sb.AppendLine("```csharp");
                sb.AppendLine(formatted.TrimEnd());
                sb.AppendLine("```");

                var diagnostics = tree.GetDiagnostics(cancellationToken)
                    .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                    .Take(8)
                    .ToList();
                if (diagnostics.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Parse/compiler notes (first few):");
                    foreach (var d in diagnostics)
                    {
                        sb.AppendLine($"- [{d.Severity}] {d.Id}: {d.GetMessage()}");
                    }
                }
            }

            if (truncatedDir)
            {
                sb.AppendLine();
                sb.AppendLine(
                    $"[!] More than {MaxDirectoryFiles} `.cs` files matched; only the first {MaxDirectoryFiles} (sorted by path) were processed.");
            }

            return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`get_code_skeleton` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCodeSkeleton failed for {Path}", path);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed: {ex.Message}");
        }
    }

    private static (List<string> Files, bool Truncated) CollectCsFilesFromDirectory(string rootDir, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        var discovered = new List<string>();
        foreach (var file in Directory.EnumerateFiles(rootDir, "*.cs", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldSkipPathByDirectorySegments(file))
            {
                continue;
            }

            discovered.Add(file);
        }

        discovered.Sort(StringComparer.OrdinalIgnoreCase);
        var truncated = discovered.Count > MaxDirectoryFiles;
        if (truncated)
        {
            discovered.RemoveRange(MaxDirectoryFiles, discovered.Count - MaxDirectoryFiles);
        }

        return (discovered, truncated);
    }

    private static bool ShouldSkipPathByDirectorySegments(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }

        for (var current = dir; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)!)
        {
            var segment = Path.GetFileName(current);
            if (segment.Length > 0 && SkippedDirectorySegments.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class SkeletonRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var stripped = StripMethodOrLocalFunctionBody(node);
            return base.VisitMethodDeclaration(stripped);
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var stripped = StripLocalFunctionBody(node);
            return base.VisitLocalFunctionStatement(stripped);
        }

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var stripped = StripConstructorBody(node);
            return base.VisitConstructorDeclaration(stripped);
        }

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var stripped = StripProperty(node);
            return base.VisitPropertyDeclaration(stripped);
        }

        private static MethodDeclarationSyntax StripMethodOrLocalFunctionBody(MethodDeclarationSyntax node)
        {
            if (node.Modifiers.Any(SyntaxKind.AbstractKeyword) || node.Modifiers.Any(SyntaxKind.ExternKeyword))
            {
                return node;
            }

            if (node.Body is null && node.ExpressionBody is null)
            {
                return node;
            }

            var n = node;
            if (n.ExpressionBody is not null)
            {
                n = n.WithExpressionBody(null).WithSemicolonToken(default);
            }

            return n.WithBody(SyntaxFactory.Block());
        }

        private static LocalFunctionStatementSyntax StripLocalFunctionBody(LocalFunctionStatementSyntax node)
        {
            if (node.Body is null && node.ExpressionBody is null)
            {
                return node;
            }

            var n = node;
            if (n.ExpressionBody is not null)
            {
                n = n.WithExpressionBody(null).WithSemicolonToken(default);
            }

            return n.WithBody(SyntaxFactory.Block());
        }

        private static ConstructorDeclarationSyntax StripConstructorBody(ConstructorDeclarationSyntax node)
        {
            if (node.Body is null && node.ExpressionBody is null)
            {
                return node;
            }

            var n = node;
            if (n.ExpressionBody is not null)
            {
                n = n.WithExpressionBody(null).WithSemicolonToken(default);
            }

            return n.WithBody(SyntaxFactory.Block());
        }

        private static PropertyDeclarationSyntax StripProperty(PropertyDeclarationSyntax node)
        {
            var n = node;
            if (n.ExpressionBody is not null)
            {
                n = n.WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithInitializer(null)
                    .WithAccessorList(CreateSingleGetAccessorList());
                return n;
            }

            if (n.AccessorList is null)
            {
                return n;
            }

            if (!AccessorListHasImplementations(n.AccessorList))
            {
                return n;
            }

            var accessorList = n.AccessorList;
            n = n.WithInitializer(null);
            var newAccessors = SyntaxFactory.List(accessorList.Accessors.Select(ToStubAccessor));
            return n.WithAccessorList(SyntaxFactory.AccessorList(newAccessors));
        }

        private static bool AccessorListHasImplementations(AccessorListSyntax list)
        {
            foreach (var acc in list.Accessors)
            {
                if (acc.Body is not null || acc.ExpressionBody is not null)
                {
                    return true;
                }
            }

            return false;
        }

        private static AccessorDeclarationSyntax ToStubAccessor(AccessorDeclarationSyntax acc)
        {
            if (acc.Body is null && acc.ExpressionBody is null)
            {
                return acc;
            }

            // Event accessors must keep a body in strict C# — use an empty block.
            if (acc.IsKind(SyntaxKind.AddAccessorDeclaration) || acc.IsKind(SyntaxKind.RemoveAccessorDeclaration))
            {
                return acc.WithBody(SyntaxFactory.Block())
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default(SyntaxToken));
            }

            return SyntaxFactory.AccessorDeclaration(acc.Kind())
                .WithAttributeLists(acc.AttributeLists)
                .WithModifiers(acc.Modifiers)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        private static AccessorListSyntax CreateSingleGetAccessorList()
        {
            var getAccessor = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
            return SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(getAccessor));
        }
    }
}
