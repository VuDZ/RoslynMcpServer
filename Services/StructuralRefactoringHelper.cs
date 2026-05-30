using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer.Services;

public sealed record StructuralRefactoringPreview(
    string Summary,
    IReadOnlyList<string> CreatedFiles,
    IReadOnlyList<string> ModifiedFiles,
    IReadOnlyList<string> Details);

public sealed class StructuralRefactoringHelper
{
    public static async Task<(Solution NewSolution, StructuralRefactoringPreview Preview)> ExtractInterfaceAsync(
        Document document,
        string className,
        string? interfaceName,
        bool createNewFile,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            throw new InvalidOperationException("Could not obtain syntax tree or semantic model.");
        }

        var classDecl = FindTopLevelType<ClassDeclarationSyntax>(root, className);
        if (classDecl is null)
        {
            throw new InvalidOperationException($"Class `{className}` was not found as a top-level declaration in the file.");
        }

        if (IsPartial(classDecl))
        {
            throw new InvalidOperationException("Extract interface does not support partial classes.");
        }

        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, cancellationToken) as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Could not resolve symbol for class `{className}`.");

        var resolvedInterfaceName = string.IsNullOrWhiteSpace(interfaceName)
            ? $"I{className}"
            : interfaceName.Trim();

        var generator = SyntaxGenerator.GetGenerator(document);
        var interfaceMemberDecls = BuildInterfaceMemberDeclarations(classSymbol, generator);
        if (interfaceMemberDecls.Count == 0)
        {
            throw new InvalidOperationException(
                $"Class `{className}` has no public instance methods or properties suitable for interface extraction.");
        }

        var interfaceDecl = SyntaxFactory.InterfaceDeclaration(resolvedInterfaceName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithMembers(SyntaxFactory.List(interfaceMemberDecls))
            .NormalizeWhitespace();

        var updatedClassDecl = AddInterfaceToClass(classDecl, resolvedInterfaceName);
        var solution = document.Project.Solution;
        var modifiedPaths = new List<string> { document.FilePath! };
        var createdPaths = new List<string>();
        var details = new List<string>
        {
            $"Interface `{resolvedInterfaceName}` with {interfaceMemberDecls.Count} member(s).",
            $"Class `{className}` will implement `{resolvedInterfaceName}`."
        };

        if (createNewFile)
        {
            var newFilePath = BuildSiblingFilePath(document.FilePath!, $"{resolvedInterfaceName}.cs");
            if (File.Exists(newFilePath))
            {
                throw new InvalidOperationException($"Target file already exists: `{newFilePath}`.");
            }

            var interfaceFileRoot = BuildNewFileRoot(root, interfaceDecl);
            var sourceEditor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            sourceEditor.ReplaceNode(classDecl, updatedClassDecl);
            solution = sourceEditor.GetChangedDocument().Project.Solution;

            var newDocId = DocumentId.CreateNewId(document.Project.Id, debugName: resolvedInterfaceName);
            solution = solution.AddDocument(
                newDocId,
                Path.GetFileName(newFilePath),
                SourceText.From(interfaceFileRoot.ToFullString(), Encoding.UTF8),
                GetDocumentFolders(document, newFilePath),
                filePath: newFilePath);

            createdPaths.Add(newFilePath);
            details.Add($"New file: `{newFilePath}`");
        }
        else
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            var container = GetMemberContainer(root);
            editor.AddMember(container, interfaceDecl);
            editor.ReplaceNode(classDecl, updatedClassDecl);
            solution = editor.GetChangedDocument().Project.Solution;
            details.Add("Interface will be added to the same file.");
        }

        foreach (var member in interfaceMemberDecls)
        {
            details.Add($"  - {member.ToFullString().Trim()}");
        }

        var preview = new StructuralRefactoringPreview(
            $"Extract interface `{resolvedInterfaceName}` from `{className}`.",
            createdPaths,
            modifiedPaths,
            details);

        return (solution, preview);
    }

    public static async Task<(Solution NewSolution, StructuralRefactoringPreview Preview)> MoveTypesToNewFilesAsync(
        Document document,
        string? typeName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            throw new InvalidOperationException("Could not obtain compilation unit.");
        }

        if (string.IsNullOrWhiteSpace(document.FilePath))
        {
            throw new InvalidOperationException("Document has no file path.");
        }

        var topLevelTypes = GetTopLevelTypeDeclarations(compilationUnit).ToList();
        if (topLevelTypes.Count <= 1)
        {
            throw new InvalidOperationException("File contains only one top-level type; nothing to move.");
        }

        var fileBaseName = Path.GetFileNameWithoutExtension(document.FilePath);
        IEnumerable<TypeDeclarationSyntax> typesToMove;
        if (string.IsNullOrWhiteSpace(typeName))
        {
            typesToMove = topLevelTypes.Where(t => !string.Equals(GetTypeIdentifier(t), fileBaseName, StringComparison.Ordinal));
        }
        else
        {
            typesToMove = topLevelTypes.Where(t => string.Equals(GetTypeIdentifier(t), typeName.Trim(), StringComparison.Ordinal));
        }

        var moveList = typesToMove.ToList();
        if (moveList.Count == 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(typeName)
                    ? $"No extra top-level types found to move (file name `{fileBaseName}.cs` matches the only declared type or no mismatched types)."
                    : $"Top-level type `{typeName}` was not found in `{document.FilePath}`.");
        }

        if (moveList.Any(IsPartial))
        {
            throw new InvalidOperationException("Move type does not support partial types.");
        }

        var solution = document.Project.Solution;
        var currentRoot = compilationUnit;
        var createdPaths = new List<string>();
        var modifiedPaths = new List<string> { document.FilePath };
        var details = new List<string>();

        foreach (var typeIdentifier in moveList.Select(GetTypeIdentifier).Distinct(StringComparer.Ordinal))
        {
            var typeToRemove = GetTopLevelTypeDeclarations(currentRoot)
                .First(t => string.Equals(GetTypeIdentifier(t), typeIdentifier, StringComparison.Ordinal));

            var newFilePath = BuildSiblingFilePath(document.FilePath, $"{typeIdentifier}.cs");
            if (File.Exists(newFilePath))
            {
                throw new InvalidOperationException($"Target file already exists: `{newFilePath}`.");
            }

            var typeFileRoot = BuildNewFileRoot(currentRoot, typeToRemove);
            var newDocId = DocumentId.CreateNewId(document.Project.Id, debugName: typeIdentifier);
            solution = solution.AddDocument(
                newDocId,
                Path.GetFileName(newFilePath),
                SourceText.From(typeFileRoot.ToFullString(), Encoding.UTF8),
                GetDocumentFolders(document, newFilePath),
                filePath: newFilePath);

            currentRoot = (CompilationUnitSyntax)currentRoot.RemoveNode(typeToRemove, SyntaxRemoveOptions.KeepNoTrivia)!;
            createdPaths.Add(newFilePath);
            details.Add($"Move `{typeIdentifier}` -> `{newFilePath}`");
        }

        currentRoot = (CompilationUnitSyntax)currentRoot.NormalizeWhitespace();
        var sourceDocId = document.Id;
        solution = solution.WithDocumentSyntaxRoot(sourceDocId, currentRoot);

        var preview = new StructuralRefactoringPreview(
            moveList.Count == 1
                ? $"Move type `{GetTypeIdentifier(moveList[0])}` to its own file."
                : $"Move {moveList.Count} top-level types to separate files.",
            createdPaths,
            modifiedPaths,
            details);

        return (solution, preview);
    }

    public static string FormatPreview(StructuralRefactoringPreview preview)
    {
        var sb = new StringBuilder();
        sb.AppendLine(preview.Summary);
        sb.AppendLine();
        if (preview.CreatedFiles.Count > 0)
        {
            sb.AppendLine($"Create ({preview.CreatedFiles.Count}):");
            foreach (var path in preview.CreatedFiles)
            {
                sb.AppendLine($"- {path}");
            }

            sb.AppendLine();
        }

        if (preview.ModifiedFiles.Count > 0)
        {
            sb.AppendLine($"Modify ({preview.ModifiedFiles.Count}):");
            foreach (var path in preview.ModifiedFiles)
            {
                sb.AppendLine($"- {path}");
            }

            sb.AppendLine();
        }

        foreach (var detail in preview.Details)
        {
            sb.AppendLine(detail);
        }

        return sb.ToString().TrimEnd();
    }

    private static List<MemberDeclarationSyntax> BuildInterfaceMemberDeclarations(INamedTypeSymbol classSymbol, SyntaxGenerator generator)
    {
        var members = new List<MemberDeclarationSyntax>();
        foreach (var member in classSymbol.GetMembers().OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(member.ContainingType, classSymbol))
            {
                continue;
            }

            SyntaxNode? declaration = member switch
            {
                IMethodSymbol { MethodKind: MethodKind.Ordinary } method => generator.MethodDeclaration(method),
                IPropertySymbol property => generator.PropertyDeclaration(property),
                IEventSymbol eventSymbol => generator.EventDeclaration(eventSymbol),
                _ => null
            };

            if (declaration is MemberDeclarationSyntax memberDecl)
            {
                members.Add(StripImplementation(memberDecl));
            }
        }

        return members;
    }

    private static MemberDeclarationSyntax StripImplementation(MemberDeclarationSyntax memberDecl)
    {
        return memberDecl switch
        {
            MethodDeclarationSyntax method => method
                .WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
            PropertyDeclarationSyntax property => property
                .WithExpressionBody(null)
                .WithInitializer(null)
                .WithAccessorList(StripAccessorBodies(property.AccessorList)),
            EventDeclarationSyntax eventDecl when eventDecl.AccessorList is not null => eventDecl
                .WithAccessorList(StripAccessorBodies(eventDecl.AccessorList)),
            _ => memberDecl
        };
    }

    private static AccessorListSyntax? StripAccessorBodies(AccessorListSyntax? accessorList)
    {
        if (accessorList is null)
        {
            return null;
        }

        var accessors = accessorList.Accessors.Select(accessor =>
            accessor
                .WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
        return accessorList.WithAccessors(SyntaxFactory.List(accessors));
    }

    private static SyntaxNode GetMemberContainer(SyntaxNode root)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            throw new InvalidOperationException("Expected compilation unit root.");
        }

        var blockNamespace = compilationUnit.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
        if (blockNamespace is not null)
        {
            return blockNamespace;
        }

        return compilationUnit;
    }

    private static ClassDeclarationSyntax AddInterfaceToClass(ClassDeclarationSyntax classDecl, string interfaceName)
    {
        var interfaceType = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(interfaceName));
        if (classDecl.BaseList is null)
        {
            return classDecl.WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(interfaceType)));
        }

        if (classDecl.BaseList.Types.Any(t => string.Equals(t.ToString(), interfaceName, StringComparison.Ordinal)))
        {
            return classDecl;
        }

        return classDecl.WithBaseList(classDecl.BaseList.AddTypes(interfaceType));
    }

    private static CompilationUnitSyntax BuildNewFileRoot(SyntaxNode sourceRoot, MemberDeclarationSyntax typeMember)
    {
        if (sourceRoot is not CompilationUnitSyntax compilationUnit)
        {
            throw new InvalidOperationException("Source root is not a compilation unit.");
        }

        var fileScopedNamespace = compilationUnit.Members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        if (fileScopedNamespace is not null)
        {
            return compilationUnit
                .WithUsings(compilationUnit.Usings)
                .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(
                [
                    fileScopedNamespace,
                    typeMember
                ]))
                .WithAttributeLists(compilationUnit.AttributeLists);
        }

        var blockNamespace = compilationUnit.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
        if (blockNamespace is not null)
        {
            var newNamespace = blockNamespace.WithMembers(SyntaxFactory.SingletonList(typeMember));
            return compilationUnit
                .WithUsings(compilationUnit.Usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNamespace))
                .WithAttributeLists(compilationUnit.AttributeLists);
        }

        return compilationUnit
            .WithUsings(compilationUnit.Usings)
            .WithMembers(SyntaxFactory.SingletonList(typeMember))
            .WithAttributeLists(compilationUnit.AttributeLists);
    }

    private static IEnumerable<TypeDeclarationSyntax> GetTopLevelTypeDeclarations(CompilationUnitSyntax root)
    {
        if (root.Members.Any(static m => m is FileScopedNamespaceDeclarationSyntax))
        {
            return root.Members.OfType<TypeDeclarationSyntax>();
        }

        foreach (var member in root.Members)
        {
            if (member is NamespaceDeclarationSyntax ns)
            {
                return ns.Members.OfType<TypeDeclarationSyntax>();
            }
        }

        return root.Members.OfType<TypeDeclarationSyntax>();
    }

    private static T? FindTopLevelType<T>(SyntaxNode root, string typeName) where T : TypeDeclarationSyntax
    {
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return null;
        }

        return GetTopLevelTypeDeclarations(compilationUnit)
            .OfType<T>()
            .FirstOrDefault(t => string.Equals(GetTypeIdentifier(t), typeName, StringComparison.Ordinal));
    }

    private static string GetTypeIdentifier(TypeDeclarationSyntax typeDecl) => typeDecl.Identifier.ValueText;

    private static bool IsPartial(TypeDeclarationSyntax typeDecl) =>
        typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword);

    private static string BuildSiblingFilePath(string sourceFilePath, string fileName)
    {
        var directory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Could not determine directory for source file.");
        return Path.GetFullPath(Path.Combine(directory, fileName));
    }

    private static IReadOnlyList<string> GetDocumentFolders(Document document, string absoluteFilePath)
    {
        if (string.IsNullOrWhiteSpace(document.Project.FilePath))
        {
            return Array.Empty<string>();
        }

        var projectDir = Path.GetDirectoryName(document.Project.FilePath)!;
        var fileDir = Path.GetDirectoryName(absoluteFilePath)!;
        if (!fileDir.StartsWith(projectDir, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        var relative = Path.GetRelativePath(projectDir, fileDir);
        if (string.IsNullOrWhiteSpace(relative) || relative == ".")
        {
            return Array.Empty<string>();
        }

        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
