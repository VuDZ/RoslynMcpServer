using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcpServer.Services.Testing.Baseline.Models;

namespace RoslynMcpServer.Services.Testing.Baseline;

internal static class BaselineDocumentSnapshotBuilder
{
    internal static BaselineDocumentSnapshot Build(SyntaxTree tree)
    {
        var root = (CompilationUnitSyntax)tree.GetRoot();
        var members = ImmutableArray.CreateBuilder<BaselineMemberSnapshot>();
        var nextOrderByType = new Dictionary<string, int>(StringComparer.Ordinal);
        var declarations = root.DescendantNodes().OfType<MemberDeclarationSyntax>().ToArray();
        var baseTypeNames = root.DescendantNodes().OfType<BaseListSyntax>().SelectMany(list => list.Types)
            .Select(baseType => SimpleName(baseType.Type)).Where(name => name.Length > 0)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var factsByType = TestTypeFactsByPath(declarations, baseTypeNames);
        foreach (var declaration in declarations)
        {
            if (declaration is BaseNamespaceDeclarationSyntax)
            {
                continue;
            }

            var syntax = DeclarationSurface(declaration);
            var hash = Hash(NamespaceScopePath(declaration) + ":"
                + DeclarationOrder(declaration, nextOrderByType) + ":" + Canonical(syntax));
            var isPartial = declaration.ChildTokens().Any(token => token.IsKind(SyntaxKind.PartialKeyword));
            foreach (var name in Names(declaration))
            {
                var snapshot = new BaselineMemberSnapshot(Key(declaration, name), hash, isPartial);
                members.Add(WithRoles(snapshot, declaration, factsByType));
            }
        }

        var imports = root.DescendantNodes().OfType<UsingDirectiveSyntax>().ToArray();
        return new BaselineDocumentSnapshot(
            Hash(tree.GetText().ToString()),
            Hash(Canonical(root.WithAttributeLists(default)
                .WithUsings(SyntaxFactory.List(root.Usings.Where(import => import.GlobalKeyword.IsKind(SyntaxKind.None))))
                .WithMembers(NamespaceSurfaces(root.Members)))),
            Hash(string.Concat(imports.Where(import => !import.GlobalKeyword.IsKind(SyntaxKind.None)).Select(Canonical))),
            Hash(string.Concat(root.AttributeLists.Select(Canonical))),
            Hash(DirectiveContent(root)),
            tree.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            members.ToImmutable())
        {
            BaseTypeNames = baseTypeNames,
        };
    }

    internal static BaselineMemberKey Key(MemberDeclarationSyntax declaration, string name)
    {
        var path = declaration.Ancestors().Reverse().Select(ancestor => ancestor switch
        {
            BaseNamespaceDeclarationSyntax space => string.Concat(space.Name.DescendantTokens().Select(token => token.Text)),
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText + "`"
                + (type is TypeDeclarationSyntax generic ? generic.TypeParameterList?.Parameters.Count ?? 0 : 0),
            _ => null,
        }).Where(part => part != null);
        return new BaselineMemberKey(
            declaration.Kind(),
            string.Join(".", path),
            name,
            declaration.ChildNodes().OfType<TypeParameterListSyntax>().FirstOrDefault()?.Parameters.Count ?? 0,
            Canonical(declaration.ChildNodes().OfType<BaseParameterListSyntax>().FirstOrDefault()),
            Canonical(declaration.ChildNodes().OfType<ExplicitInterfaceSpecifierSyntax>().FirstOrDefault()));
    }

    private static Dictionary<string, TestTypeFacts> TestTypeFactsByPath(
        IEnumerable<MemberDeclarationSyntax> declarations, ImmutableHashSet<string> baseTypeNames)
    {
        var result = new Dictionary<string, TestTypeFacts>(StringComparer.Ordinal);
        foreach (var parts in declarations.OfType<TypeDeclarationSyntax>().GroupBy(DeclaredTypePath))
        {
            var methods = parts.SelectMany(type => type.Members).OfType<MethodDeclarationSyntax>().ToArray();
            var hasOwnTests = methods.Any(IsTestMethod);
            var markedAsTestType = parts.SelectMany(type => type.AttributeLists).SelectMany(list => list.Attributes)
                .Any(attribute => AttributeName(attribute) is "TestClass" or "TestFixture");
            // Inherited roots are shared; R-17 (docs/rapid-tia/README.md) favors extra expansion on simple-name matches.
            var blocksNarrowing = parts.Any(type => type.Modifiers.Any(SyntaxKind.AbstractKeyword))
                || baseTypeNames.Contains(parts.First().Identifier.ValueText)
                // Nested code can read private outer state, so its tests also forbid outer-only narrowing under R-17.
                || parts.SelectMany(type => type.Members).Any(member => member.Kind() is SyntaxKind.ClassDeclaration
                    or SyntaxKind.StructDeclaration or SyntaxKind.RecordDeclaration or SyntaxKind.RecordStructDeclaration
                    or SyntaxKind.InterfaceDeclaration);
            var contracts = parts.SelectMany(type => type.BaseList?.Types.Select(baseType => SimpleName(baseType.Type)) ?? [])
                .ToHashSet(StringComparer.Ordinal);
            var overloadedNames = methods.GroupBy(method => method.Identifier.ValueText).Where(group => group.Count() > 1)
                .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            result.Add(parts.Key, new TestTypeFacts(hasOwnTests || markedAsTestType, hasOwnTests,
                blocksNarrowing, contracts, overloadedNames));
        }
        return result;
    }

    private static string DeclaredTypePath(TypeDeclarationSyntax type)
    {
        var parentPath = Key(type, string.Empty).ContainingTypePath;
        return (parentPath.Length == 0 ? "" : parentPath + ".") + type.Identifier.ValueText
            + "`" + (type.TypeParameterList?.Parameters.Count ?? 0);
    }

    private static BaselineMemberSnapshot WithRoles(
        BaselineMemberSnapshot snapshot,
        MemberDeclarationSyntax declaration,
        Dictionary<string, TestTypeFacts> factsByType)
    {
        var isTest = declaration is MethodDeclarationSyntax method && IsTestMethod(method);
        var isExtension = declaration is MethodDeclarationSyntax extension
            && extension.ParameterList.Parameters.FirstOrDefault()?.Modifiers.Any(SyntaxKind.ThisKeyword) == true;
        var isRoot = false;
        var isTestType = false;
        string? contextHash = null;
        var containingType = factsByType.GetValueOrDefault(snapshot.Key.ContainingTypePath);
        if (containingType is { IsTestType: true, BlocksNarrowing: false })
        {
            isRoot = declaration is ConstructorDeclarationSyntax constructor && !constructor.Modifiers.Any(SyntaxKind.StaticKeyword)
                || declaration is MethodDeclarationSyntax lifecycle && IsLifecycleMethod(lifecycle,
                    containingType.Contracts, hasOverloads: containingType.OverloadedNames.Contains(lifecycle.Identifier.ValueText));
            // Constants can be inlined into other classes; they are not executed test-class initialization roots.
            if (declaration is BaseFieldDeclarationSyntax field && !field.Modifiers.Any(SyntaxKind.ConstKeyword))
            {
                isRoot = IsPrivateStaticOrInstance(field.Modifiers)
                    && field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == snapshot.Key.Name
                        && variable.Initializer != null);
                // Track declaration context separately from the initializer; the detector also checks the baseline role.
                contextHash = Hash(Canonical(field.WithDeclaration(field.Declaration.WithVariables(
                    SyntaxFactory.SeparatedList(field.Declaration.Variables.Select(variable => variable.WithInitializer(null)))))));
            }
            else if (declaration is PropertyDeclarationSyntax property)
            {
                isRoot = property.Initializer != null && IsPrivateStaticOrInstance(property.Modifiers);
                contextHash = Hash(Canonical(property.WithInitializer(null).WithSemicolonToken(default)));
            }
        }

        if (declaration is TypeDeclarationSyntax type && factsByType.TryGetValue(DeclaredTypePath(type), out var ownType))
        {
            isTestType = ownType.IsTestType;
            isRoot = ownType.HasOwnTests && !ownType.BlocksNarrowing && !snapshot.IsPartial;
        }

        return snapshot with
        {
            IsTestMethod = isTest,
            IsTestType = isTestType,
            IsOwnTestClassRoot = isRoot,
            IsExtensionMethod = isExtension,
            BlocksNarrowing = isTest && containingType?.BlocksNarrowing == true,
            InitializerContextHash = contextHash,
        };
    }

    // R-17: static state can escape the test class unless access is private-only; private protected also escapes.
    private static bool IsPrivateStaticOrInstance(SyntaxTokenList modifiers) => !modifiers.Any(SyntaxKind.StaticKeyword)
        || !modifiers.Any(token => token.IsKind(SyntaxKind.PublicKeyword) || token.IsKind(SyntaxKind.ProtectedKeyword)
            || token.IsKind(SyntaxKind.InternalKeyword));

    private static bool IsTestMethod(MethodDeclarationSyntax method) => method.AttributeLists
        .SelectMany(list => list.Attributes).Any(attribute => TestAttributeMatcher.IsTestAttributeSyntaxName(attribute.Name.ToString()));

    private static bool IsLifecycleMethod(MethodDeclarationSyntax method, HashSet<string> contracts, bool hasOverloads)
    {
        if (method.AttributeLists.SelectMany(list => list.Attributes).Any(attribute => AttributeName(attribute) is
            "SetUp" or "TearDown" or "OneTimeSetUp" or "OneTimeTearDown"
            or "TestInitialize" or "TestCleanup" or "ClassInitialize" or "ClassCleanup"))
        {
            return true;
        }

        // A name alone cannot select a lifecycle implementation over competing helpers without semantic binding.
        if (method.ParameterList.Parameters.Count != 0 || method.TypeParameterList?.Parameters.Count > 0
            || method.Modifiers.Any(SyntaxKind.StaticKeyword)
            || hasOverloads && method.ExplicitInterfaceSpecifier == null)
        {
            return false;
        }

        var explicitContract = method.ExplicitInterfaceSpecifier == null
            ? null : SimpleName(method.ExplicitInterfaceSpecifier.Name);
        return method.Identifier.ValueText switch
        {
            "InitializeAsync" => contracts.Contains("IAsyncLifetime") && explicitContract is null or "IAsyncLifetime",
            "DisposeAsync" => contracts.Contains("IAsyncLifetime") && explicitContract is null or "IAsyncLifetime"
                || contracts.Contains("IAsyncDisposable") && explicitContract is null or "IAsyncDisposable",
            "Dispose" => contracts.Contains("IDisposable") && explicitContract is null or "IDisposable",
            _ => false,
        };
    }

    private static string AttributeName(AttributeSyntax attribute)
    {
        var name = SimpleName(attribute.Name);
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
    }

    private static string SimpleName(TypeSyntax type) => type switch
    {
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax qualified => qualified.Name.Identifier.ValueText,
        PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
        _ => "",
    };

    private static SyntaxList<MemberDeclarationSyntax> NamespaceSurfaces(SyntaxList<MemberDeclarationSyntax> members) =>
        SyntaxFactory.List(members.OfType<BaseNamespaceDeclarationSyntax>().Select(declaration =>
            (MemberDeclarationSyntax)declaration.WithMembers(NamespaceSurfaces(declaration.Members))));

    // Repeated namespace blocks can bind identical declarations to different imports (docs/rapid-tia/README.md).
    private static string NamespaceScopePath(MemberDeclarationSyntax declaration) =>
        string.Join("/", declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(space =>
            space.Parent!.ChildNodes().OfType<BaseNamespaceDeclarationSyntax>().TakeWhile(sibling => sibling != space)
                .Count().ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static SyntaxNode DeclarationSurface(MemberDeclarationSyntax declaration) => declaration switch
    {
        TypeDeclarationSyntax type => type.WithMembers(default),
        EnumDeclarationSyntax enumeration => enumeration.WithMembers(default),
        _ => declaration,
    };

    // Order affects enum values, layout, initializers, record PrintMembers and property serialization, even across partial parts.
    private static string DeclarationOrder(MemberDeclarationSyntax declaration, Dictionary<string, int> nextOrderByType)
    {
        if (declaration is not (BaseFieldDeclarationSyntax or EnumMemberDeclarationSyntax or PropertyDeclarationSyntax))
        {
            return string.Empty;
        }

        var typePath = Key(declaration, string.Empty).ContainingTypePath;
        nextOrderByType.TryGetValue(typePath, out var order);
        nextOrderByType[typePath] = order + 1;
        return order.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IEnumerable<string> Names(MemberDeclarationSyntax declaration) => declaration switch
    {
        BaseFieldDeclarationSyntax field => field.Declaration.Variables.Select(variable => variable.Identifier.ValueText),
        BaseTypeDeclarationSyntax type => [type.Identifier.ValueText],
        DelegateDeclarationSyntax member => [member.Identifier.ValueText],
        MethodDeclarationSyntax member => [member.Identifier.ValueText],
        ConstructorDeclarationSyntax member => [member.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor"],
        DestructorDeclarationSyntax => [".dtor"],
        PropertyDeclarationSyntax member => [member.Identifier.ValueText],
        EventDeclarationSyntax member => [member.Identifier.ValueText],
        IndexerDeclarationSyntax => ["this"],
        OperatorDeclarationSyntax member => [member.CheckedKeyword.Text + member.OperatorToken.Text],
        ConversionOperatorDeclarationSyntax member => [member.ImplicitOrExplicitKeyword.Text
            + member.CheckedKeyword.Text + Canonical(member.Type)],
        EnumMemberDeclarationSyntax member => [member.Identifier.ValueText],
        GlobalStatementSyntax member => [member.Parent!.ChildNodes().OfType<GlobalStatementSyntax>()
            .TakeWhile(sibling => sibling != member).Count().ToString(System.Globalization.CultureInfo.InvariantCulture)],
        // Unknown or recovered declarations still get an entry; parse errors trigger conservative file handling.
        _ => [Canonical(DeclarationSurface(declaration))],
    };

    private static string Canonical(SyntaxNode? node)
    {
        var builder = new StringBuilder();
        if (node != null)
        {
            foreach (var token in node.DescendantTokens())
            {
                AppendTrivia(builder, token.LeadingTrivia);
                AppendToken(builder, token);
                AppendTrivia(builder, token.TrailingTrivia);
            }
        }
        return builder.ToString();
    }

    private static string DirectiveContent(SyntaxNode root)
    {
        var builder = new StringBuilder();
        AppendTrivia(builder, root.DescendantTrivia());
        return builder.ToString();
    }

    private static void AppendTrivia(StringBuilder builder, IEnumerable<SyntaxTrivia> triviaList)
    {
        foreach (var trivia in triviaList)
        {
            if (trivia.IsDirective)
            {
                builder.Append(Canonical(trivia.GetStructure()));
            }
            else if (trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            {
                // Disabled text is trivia, but edits there must survive trivia removal (rapid-tia/README.md).
                foreach (var token in SyntaxFactory.ParseTokens(trivia.ToFullString()))
                {
                    // ParseTokens adds an artificial EOF; hashing it made comment-only edits in empty inactive branches visible.
                    if (!token.IsKind(SyntaxKind.EndOfFileToken))
                    {
                        AppendToken(builder, token);
                    }
                }
            }
            else if (trivia.IsKind(SyntaxKind.PreprocessingMessageTrivia))
            {
                var message = string.Join(" ", trivia.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                builder.Append(trivia.RawKind).Append(':').Append(message.Length).Append(':').Append(message).Append(';');
            }
        }
    }

    private static void AppendToken(StringBuilder builder, SyntaxToken token) =>
        builder.Append(token.RawKind).Append(':').Append(token.Text.Length).Append(':').Append(token.Text).Append(';');

    // Hash UTF-16 code units so the checksum preserves the exact text, including unpaired surrogates.
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(text.AsSpan())));

    private sealed record TestTypeFacts(
        bool IsTestType,
        bool HasOwnTests,
        bool BlocksNarrowing,
        HashSet<string> Contracts,
        HashSet<string> OverloadedNames);
}
