using System.ComponentModel;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;

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
    [Description("Extracts the high-level skeleton (contract) of a C# file. Returns namespaces, classes, interfaces, properties, and method signatures, but COMPLETELY OMITS method bodies to save LLM context. Use this before modifying any class.")]
    public async Task<string> GetClassSkeleton(
        [Description("Path to a .cs file (same argument name `filePath` as get_file_content).")]
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var document = await _solutionManager.FindDocumentAsync(filePath, cancellationToken);
        if (document is null)
        {
            return ToolTelemetry.TraceAndReturn(
                nameof(GetClassSkeleton),
                $"The file was not found in the workspace: `{filePath}`. Load the solution or load_workspace first, or verify the path.");
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
        if (semanticModel is null || syntaxRoot is null)
        {
            return ToolTelemetry.TraceAndReturn(
                nameof(GetClassSkeleton),
                $"Could not obtain semantic model or syntax tree for file: `{filePath}`.");
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
    [Description("Returns Roslyn compiler diagnostics for a single C# file from the active workspace. Includes only Warning and Error diagnostics.")]
    public async Task<string> GetDiagnosticsForFile(
        [Description("Absolute path to the target .cs file (same parameter name as get_file_content / find_symbol_references).")] string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(GetDiagnosticsForFile), "Error: `filePath` is empty.");
            }

            var fullPath = Path.GetFullPath(filePath);
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
                    var line = d.Location.GetLineSpan().StartLinePosition.Line + 1;
                    return $"[{d.Severity}] Line {line}: ({d.Id}) {d.GetMessage()}";
                })
                .ToList();

            return ToolTelemetry.TraceAndReturn(nameof(GetDiagnosticsForFile), string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetDiagnosticsForFile failed for {FilePath}", filePath);
            return ToolTelemetry.TraceAndReturn(nameof(GetDiagnosticsForFile), $"Failed to get diagnostics: {ex.Message}");
        }
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
