using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Workspace.StateCache.Models;

namespace RoslynMcpServer.Services.Workspace.StateCache;

/// <summary>
/// Public option facts for the Debug base-query profile.
/// Parser-owned state without a getter is refused instead of replaced with a default.
/// </summary>
internal static class SnapshotOptionFacts
{
    public static SnapshotParseOptions Parse(CSharpParseOptions original)
    {
        ArgumentNullException.ThrowIfNull(original);

        var dto = new SnapshotParseOptions(
            original.SpecifiedLanguageVersion.ToString(),
            original.LanguageVersion.ToString(),
            original.DocumentationMode.ToString(),
            original.Kind.ToString(),
            original.PreprocessorSymbolNames.ToArray(),
            Pairs(original.Features));
        var reconstructed = new CSharpParseOptions(
                Enum.Parse<LanguageVersion>(dto.SpecifiedLanguageVersion),
                Enum.Parse<DocumentationMode>(dto.DocumentationMode),
                Enum.Parse<SourceCodeKind>(dto.Kind),
                dto.PreprocessorSymbols)
            .WithFeatures(dto.Features.Select(pair => KeyValuePair.Create(pair.Name, pair.Value)));
        Require(
            reconstructed.LanguageVersion.ToString() == dto.LanguageVersion && reconstructed.Equals(original),
            "parse-option-witness-unavailable");
        return dto;
    }

    public static SnapshotCompilationOptions Compilation(
        Project project,
        CompilationOptions? descriptorOptions,
        string strongNamePolicy)
    {
        ArgumentNullException.ThrowIfNull(project);
        Require(strongNamePolicy == SnapshotSigningProfile.IndependentPolicy, "unknown-strong-name-policy");
        Require(project.CompilationOptions is CSharpCompilationOptions, "non-csharp-options");
        var original = (CSharpCompilationOptions)project.CompilationOptions!;
        var directory = Path.GetDirectoryName(project.FilePath!);
        Require(directory is not null, "project-directory-unavailable");
        var factory = CommandLineProject.CreateProjectInfo(
            project.Name,
            LanguageNames.CSharp,
            Array.Empty<string>(),
            directory,
            project.Solution.Workspace).CompilationOptions;
        Require(factory is not null, "metadata-resolver-factory-witness-unavailable");
        Require(
            original.MetadataReferenceResolver is not null
            && original.MetadataReferenceResolver.Equals(factory.MetadataReferenceResolver),
            "metadata-resolver-factory-witness-unavailable");
        Require(original.StrongNameProvider is null or DesktopStrongNameProvider, "unknown-strong-name-provider");
        Require(descriptorOptions?.SyntaxTreeOptionsProvider is null, "custom-descriptor-tree-options-unsupported");
        var treeProvider = original.SyntaxTreeOptionsProvider;
        Require(
            treeProvider is null || (descriptorOptions is not null
                && treeProvider.GetType().Assembly == typeof(Microsoft.CodeAnalysis.Workspace).Assembly
                && treeProvider.GetType().FullName == "Microsoft.CodeAnalysis.ProjectState+ProjectSyntaxTreeOptionsProvider"),
            "syntax-tree-option-provider-witness-unavailable");
        var comparer = ReferenceEquals(original.AssemblyIdentityComparer, DesktopAssemblyIdentityComparer.Default)
            ? "desktop-default"
            : ReferenceEquals(original.AssemblyIdentityComparer, AssemblyIdentityComparer.Default) ? "default" : "unknown";
        Require(comparer != "unknown", "unknown-assembly-identity-comparer");
        var dto = new SnapshotCompilationOptions(
            "csharp-command-line-debug-plus-v1",
            original.OutputKind.ToString(),
            original.ModuleName,
            original.MainTypeName,
            original.ScriptClassName,
            original.Usings.ToArray(),
            original.OptimizationLevel.ToString(),
            original.CheckOverflow,
            original.AllowUnsafe,
            original.CryptoKeyContainer,
            original.CryptoKeyFile,
            original.CryptoPublicKey.IsDefault ? [] : original.CryptoPublicKey.ToArray(),
            original.DelaySign,
            original.PublicSign,
            original.Platform.ToString(),
            original.GeneralDiagnosticOption.ToString(),
            original.WarningLevel,
            Pairs(original.SpecificDiagnosticOptions.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.ToString()))),
            original.ConcurrentBuild,
            original.Deterministic,
            original.MetadataImportOptions.ToString(),
            original.NullableContextOptions.ToString(),
            original.ReportSuppressedDiagnostics,
            comparer,
            Source(original.SourceReferenceResolver),
            Xml(original.XmlReferenceResolver),
            new SnapshotResolver("command-line-project-factory-v1", directory, [], []),
            strongNamePolicy,
            treeProvider is null
                ? "none"
                : "public-descriptor-null-provider-fresh-project-analyzer-configs-v1");
        var restored = Reconstruct(dto, factory.MetadataReferenceResolver);
        // The profile and the public host factory bound these identities on purpose.
        // Every other public value, including parser-owned state, still has to pass Equals.
        restored = restored.WithStrongNameProvider(original.StrongNameProvider)
            .WithMetadataReferenceResolver(original.MetadataReferenceResolver)
            .WithSyntaxTreeOptionsProvider(original.SyntaxTreeOptionsProvider);
        Require(restored.Equals(original), "compilation-option-witness-unavailable");
        return dto;
    }

    public static CSharpParseOptions ReconstructParse(SnapshotParseOptions dto)
    {
        var options = new CSharpParseOptions(
                Enum.Parse<LanguageVersion>(dto.SpecifiedLanguageVersion),
                Enum.Parse<DocumentationMode>(dto.DocumentationMode),
                Enum.Parse<SourceCodeKind>(dto.Kind),
                dto.PreprocessorSymbols)
            .WithFeatures(dto.Features.Select(pair => KeyValuePair.Create(pair.Name, pair.Value)));
        Require(options.LanguageVersion.ToString() == dto.LanguageVersion && !options.Errors.Any(),
            "parse-option-witness-unavailable");
        return options;
    }

    public static CSharpCompilationOptions ReconstructCompilation(
        SnapshotProject project,
        Microsoft.CodeAnalysis.Workspace workspace)
    {
        var dto = project.CompilationOptions;
        var factory = CommandLineProject.CreateProjectInfo(
            project.Name,
            LanguageNames.CSharp,
            Array.Empty<string>(),
            dto.MetadataResolver.BaseDirectory!,
            workspace).CompilationOptions;
        Require(factory?.MetadataReferenceResolver is not null, "metadata-resolver-factory-witness-unavailable");
        return Reconstruct(dto, factory.MetadataReferenceResolver);
    }

    private static CSharpCompilationOptions Reconstruct(
        SnapshotCompilationOptions dto,
        MetadataReferenceResolver? metadataResolver)
    {
        Require(dto.ConstructionWitness == "csharp-command-line-debug-plus-v1", "unknown-option-witness");
        Require(dto.AssemblyIdentityComparer is "desktop-default" or "default", "unknown-assembly-identity-comparer");
        Require(dto.SourceResolver.Kind is "none" or "source-file-v1", "unknown-source-resolver");
        Require(dto.XmlResolver.Kind is "none" or "xml-file-v1", "unknown-xml-resolver");
        Require(dto.MetadataResolver.Kind == "command-line-project-factory-v1", "unknown-metadata-resolver");
        Require(dto.StrongNamePolicy == SnapshotSigningProfile.IndependentPolicy, "unknown-strong-name-policy");
        Require(
            dto.SyntaxTreeOptionsPolicy is "none" or "public-descriptor-null-provider-fresh-project-analyzer-configs-v1",
            "unknown-tree-options-policy");
        Require(dto.MetadataResolver.BaseDirectory is not null, "metadata-resolver-factory-witness-unavailable");
        return CSharpCommandLineParser.Default.Parse(["/debug+"], dto.MetadataResolver.BaseDirectory, null)
            .CompilationOptions
            .WithOutputKind(Enum.Parse<OutputKind>(dto.OutputKind))
            .WithModuleName(dto.ModuleName)
            .WithMainTypeName(dto.MainTypeName)
            .WithScriptClassName(dto.ScriptClassName)
            .WithUsings(dto.Usings)
            .WithOptimizationLevel(Enum.Parse<OptimizationLevel>(dto.OptimizationLevel))
            .WithOverflowChecks(dto.CheckOverflow)
            .WithAllowUnsafe(dto.AllowUnsafe)
            .WithCryptoKeyContainer(dto.CryptoKeyContainer)
            .WithCryptoKeyFile(dto.CryptoKeyFile)
            .WithCryptoPublicKey(dto.CryptoPublicKey.ToImmutableArray())
            .WithDelaySign(dto.DelaySign)
            .WithPublicSign(dto.PublicSign)
            .WithPlatform(Enum.Parse<Platform>(dto.Platform))
            .WithGeneralDiagnosticOption(Enum.Parse<ReportDiagnostic>(dto.GeneralDiagnosticOption))
            .WithWarningLevel(dto.WarningLevel)
            .WithSpecificDiagnosticOptions(dto.SpecificDiagnosticOptions.Select(pair =>
                KeyValuePair.Create(pair.Name, Enum.Parse<ReportDiagnostic>(pair.Value))))
            .WithConcurrentBuild(dto.ConcurrentBuild)
            .WithDeterministic(dto.Deterministic)
            .WithMetadataImportOptions(Enum.Parse<MetadataImportOptions>(dto.MetadataImportOptions))
            .WithNullableContextOptions(Enum.Parse<NullableContextOptions>(dto.NullableContextOptions))
            .WithReportSuppressedDiagnostics(dto.ReportSuppressedDiagnostics)
            .WithAssemblyIdentityComparer(dto.AssemblyIdentityComparer == "desktop-default"
                ? DesktopAssemblyIdentityComparer.Default
                : AssemblyIdentityComparer.Default)
            .WithSourceReferenceResolver(dto.SourceResolver.Kind == "none"
                ? null
                : new SourceFileResolver(
                    dto.SourceResolver.SearchPaths.ToImmutableArray(),
                    dto.SourceResolver.BaseDirectory,
                    dto.SourceResolver.PathMap.Select(pair => KeyValuePair.Create(pair.Name, pair.Value)).ToImmutableArray()))
            .WithXmlReferenceResolver(dto.XmlResolver.Kind == "none"
                ? null
                : new XmlFileResolver(dto.XmlResolver.BaseDirectory))
            .WithMetadataReferenceResolver(metadataResolver)
            .WithStrongNameProvider(new DesktopStrongNameProvider(ImmutableArray<string>.Empty));
    }

    private static SnapshotResolver Source(SourceReferenceResolver? original)
    {
        Require(original is null or SourceFileResolver, "unknown-source-resolver");
        return original is SourceFileResolver resolver
            ? new SnapshotResolver(
                "source-file-v1",
                resolver.BaseDirectory,
                resolver.SearchPaths.ToArray(),
                Pairs(resolver.PathMap))
            : new SnapshotResolver("none", null, [], []);
    }

    private static SnapshotResolver Xml(XmlReferenceResolver? original)
    {
        Require(original is null or XmlFileResolver, "unknown-xml-resolver");
        return original is XmlFileResolver resolver
            ? new SnapshotResolver("xml-file-v1", resolver.BaseDirectory, [], [])
            : new SnapshotResolver("none", null, [], []);
    }

    private static SnapshotNameValue[] Pairs(IEnumerable<KeyValuePair<string, string>> values) =>
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SnapshotNameValue(pair.Key, pair.Value))
            .ToArray();

    private static void Require([DoesNotReturnIf(false)] bool condition, string reason)
    {
        if (!condition)
        {
            throw new InvalidOperationException(reason);
        }
    }
}
