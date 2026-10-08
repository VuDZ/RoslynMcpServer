using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Workspace.StateCache.Models;

namespace RoslynMcpServer.Services.Workspace.StateCache;

/// <summary>
/// Encodes and decodes a schema-v1 <see cref="WorkspaceSnapshot"/> as a bounded UTF-8 envelope.
/// The envelope checksum is compared before the payload is materialized, so a damaged envelope
/// never becomes a snapshot a later restore could apply.
/// This type does not read a cache directory, lock files, scan inputs, or restore a workspace.
/// </summary>
internal static class WorkspaceSnapshotCodec
{
    public const string EnvelopeSchema = "workspace-snapshot-envelope-v1";

    public const string ChecksumAlgorithmName = "SHA256";

    public const int MaximumBytes = 16 * 1024 * 1024;

    internal const int MaximumStringLength = 32768;

    internal const int MaximumArrayItems = 1_000_000;

    internal const int MaximumMemberships = 1_000_000;

    internal static IEqualityComparer<SnapshotProjectKey> ProjectKeyIdentity => ProjectKeyComparer.Instance;

    private static int PayloadByteLimit => MaximumBytes - EnvelopeOverhead;

    public static byte[] Encode(WorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        var payload = SerializePayload(snapshot);
        var envelope = BuildEnvelope(payload);
        _ = Decode(envelope);
        return envelope;
    }

    public static WorkspaceSnapshot Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaximumBytes)
        {
            throw new InvalidDataException("The snapshot exceeds the byte bound.");
        }

        try
        {
            RejectMalformedJson(payload);
            using var document = JsonDocument.Parse(payload.ToArray(), DocumentOptions);
            var root = document.RootElement;
            RequireShape(root, "Schema", "ChecksumAlgorithm", "Checksum", "Payload");
            Require(root.GetProperty("Schema").GetString() == EnvelopeSchema, "The snapshot schema is unsupported.");
            Require(
                root.GetProperty("ChecksumAlgorithm").GetString() == ChecksumAlgorithmName,
                "The snapshot checksum algorithm is unsupported.");
            var body = root.GetProperty("Payload");
            Require(body.ValueKind == JsonValueKind.Object, "An object is required.");
            // Hash the payload text before materializing it. A mismatch leaves no snapshot to restore.
            var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.GetRawText())));
            Require(root.GetProperty("Checksum").GetString() == actual, "The snapshot checksum does not match the payload.");
            var total = 0;
            ValidatePayloadShape(body, ref total);
            var snapshot = body.Deserialize<WorkspaceSnapshot>(SerializerOptions)
                ?? throw new InvalidDataException("The snapshot payload is null.");
            ValidateSnapshot(snapshot);
            return snapshot;
        }
        catch (Exception exception) when (exception is not InvalidDataException && IsMalformed(exception))
        {
            throw new InvalidDataException("The snapshot payload is truncated or malformed.", exception);
        }
    }

    private static void RejectMalformedJson(ReadOnlySpan<byte> payload)
    {
        var reader = new Utf8JsonReader(payload, ReaderOptions);
        var names = new Stack<HashSet<string>>();
        var sawValue = false;
        while (true)
        {
            bool read;
            try
            {
                read = reader.Read();
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The snapshot payload is truncated or malformed.", exception);
            }

            if (!read)
            {
                break;
            }

            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    sawValue = true;
                    names.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    Require(names.Count > 0, "The snapshot payload is truncated or malformed.");
                    names.Pop();
                    break;
                case JsonTokenType.StartArray:
                    sawValue = true;
                    break;
                case JsonTokenType.PropertyName:
                    var name = reader.GetString();
                    Require(name is not null && name.Length <= MaximumStringLength, "An object contains an oversized key.");
                    Require(names.Count > 0 && names.Peek().Add(name), "An object contains a duplicate key.");
                    break;
                case JsonTokenType.String:
                    Require((reader.GetString()?.Length ?? 0) <= MaximumStringLength, "A string exceeds the resource bound.");
                    break;
            }
        }

        Require(sawValue && names.Count == 0 && reader.BytesConsumed == payload.Length, "The snapshot payload is truncated or malformed.");
    }

    private static void ValidatePayloadShape(JsonElement snapshot, ref int total)
    {
        RequireShape(
            snapshot,
            "SchemaVersion",
            "Profile",
            "RoslynVersion",
            "WorkspacePath",
            "Properties",
            "Projects",
            "InputPaths");
        Require(
            snapshot.GetProperty("SchemaVersion").TryGetInt32(out var version) && version == OrdinaryWorkspaceCapture.SchemaVersion,
            "The snapshot schema is unsupported.");
        Require(snapshot.GetProperty("Profile").GetString() == OrdinaryWorkspaceCapture.Profile, "The snapshot schema is unsupported.");
        ValidatePropertiesShape(snapshot.GetProperty("Properties"));
        foreach (var project in BoundArray(snapshot, "Projects", ref total))
        {
            ValidateProjectShape(project, ref total);
        }

        foreach (var path in BoundArray(snapshot, "InputPaths", ref total))
        {
            ValidateInputPathShape(path, ref total);
        }
    }

    private static void ValidatePropertiesShape(JsonElement properties)
    {
        RequireShape(properties, "RawRequested", "LoaderGlobals");
        ValidatePropertySetShape(properties.GetProperty("RawRequested"));
        ValidatePropertySetShape(properties.GetProperty("LoaderGlobals"));
    }

    private static void ValidatePropertySetShape(JsonElement set) =>
        RequireShape(set, "Configuration", "Platform", "TargetFramework");

    private static void ValidateProjectShape(JsonElement project, ref int total)
    {
        RequireShape(
            project,
            "Key",
            "EvaluatedProperties",
            "Name",
            "AssemblyName",
            "Language",
            "OutputFilePath",
            "OutputRefFilePath",
            "DefaultNamespace",
            "CompilationAssemblyPath",
            "GeneratedFilesOutputDirectory",
            "ParseOptions",
            "CompilationOptions",
            "Documents",
            "ProjectReferences",
            "MetadataReferences",
            "AnalyzerReferences");
        ValidateKeyShape(project.GetProperty("Key"));
        ValidateEvaluatedShape(project.GetProperty("EvaluatedProperties"), ref total);
        ValidateParseShape(project.GetProperty("ParseOptions"), ref total);
        ValidateCompilationShape(project.GetProperty("CompilationOptions"), ref total);
        foreach (var document in BoundArray(project, "Documents", ref total))
        {
            ValidateDocumentShape(document, ref total);
        }

        foreach (var reference in BoundArray(project, "ProjectReferences", ref total))
        {
            RequireShape(reference, "Target", "Aliases", "EmbedInteropTypes");
            ValidateKeyShape(reference.GetProperty("Target"));
            WalkStrings(reference, "Aliases", ref total);
        }

        foreach (var reference in BoundArray(project, "MetadataReferences", ref total))
        {
            RequireShape(reference, "Path", "Kind", "Aliases", "EmbedInteropTypes", "BinaryDocumentationPolicy");
            WalkStrings(reference, "Aliases", ref total);
        }

        foreach (var reference in BoundArray(project, "AnalyzerReferences", ref total))
        {
            RequireShape(reference, "OriginalPath", "IdentitySource");
        }
    }

    private static void ValidateEvaluatedShape(JsonElement evaluated, ref int total)
    {
        RequireShape(evaluated, "Values", "Source", "UnknownProperties");
        ValidatePropertySetShape(evaluated.GetProperty("Values"));
        WalkStrings(evaluated, "UnknownProperties", ref total);
    }

    private static void ValidateParseShape(JsonElement options, ref int total)
    {
        RequireShape(options, "SpecifiedLanguageVersion", "LanguageVersion", "DocumentationMode", "Kind", "PreprocessorSymbols", "Features");
        WalkStrings(options, "PreprocessorSymbols", ref total);
        WalkNameValues(options, "Features", ref total);
    }

    private static void ValidateCompilationShape(JsonElement options, ref int total)
    {
        RequireShape(
            options,
            "ConstructionWitness",
            "OutputKind",
            "ModuleName",
            "MainTypeName",
            "ScriptClassName",
            "Usings",
            "OptimizationLevel",
            "CheckOverflow",
            "AllowUnsafe",
            "CryptoKeyContainer",
            "CryptoKeyFile",
            "CryptoPublicKey",
            "DelaySign",
            "PublicSign",
            "Platform",
            "GeneralDiagnosticOption",
            "WarningLevel",
            "SpecificDiagnosticOptions",
            "ConcurrentBuild",
            "Deterministic",
            "MetadataImportOptions",
            "NullableContextOptions",
            "ReportSuppressedDiagnostics",
            "AssemblyIdentityComparer",
            "SourceResolver",
            "XmlResolver",
            "MetadataResolver",
            "StrongNamePolicy",
            "SyntaxTreeOptionsPolicy");
        WalkStrings(options, "Usings", ref total);
        WalkNameValues(options, "SpecificDiagnosticOptions", ref total);
        ValidateResolverShape(options.GetProperty("SourceResolver"), ref total);
        ValidateResolverShape(options.GetProperty("XmlResolver"), ref total);
        ValidateResolverShape(options.GetProperty("MetadataResolver"), ref total);
    }

    private static void ValidateResolverShape(JsonElement resolver, ref int total)
    {
        RequireShape(resolver, "Kind", "BaseDirectory", "SearchPaths", "PathMap");
        WalkStrings(resolver, "SearchPaths", ref total);
        WalkNameValues(resolver, "PathMap", ref total);
    }

    private static void ValidateDocumentShape(JsonElement document, ref int total)
    {
        RequireShape(document, "Kind", "Path", "Name", "Folders", "SourceCodeKind", "IsGenerated", "TextMetadata");
        WalkStrings(document, "Folders", ref total);
        RequireShape(document.GetProperty("TextMetadata"), "EncodingCodePage", "EncodingPreamble", "ChecksumAlgorithm", "Checksum");
    }

    private static void ValidateInputPathShape(JsonElement path, ref int total)
    {
        RequireShape(path, "Path", "Occurrences");
        foreach (var occurrence in BoundArray(path, "Occurrences", ref total))
        {
            RequireShape(occurrence, "Owner", "DocumentKind", "Role", "RoleSource", "Producer", "ProducerSource", "Present");
            ValidateKeyShape(occurrence.GetProperty("Owner"));
            var producer = occurrence.GetProperty("Producer");
            if (producer.ValueKind != JsonValueKind.Null)
            {
                ValidateKeyShape(producer);
            }
        }
    }

    private static void ValidateKeyShape(JsonElement key) =>
        RequireShape(key, "ProjectPath", "Configuration", "Platform", "TargetFramework");

    private static void WalkStrings(JsonElement owner, string name, ref int total)
    {
        foreach (var item in BoundArray(owner, name, ref total))
        {
            Require(item.ValueKind == JsonValueKind.String, "A string array member is invalid.");
            Require(item.GetString()!.Length <= MaximumStringLength, "A string exceeds the resource bound.");
        }
    }

    private static void WalkNameValues(JsonElement owner, string name, ref int total)
    {
        foreach (var pair in BoundArray(owner, name, ref total))
        {
            RequireShape(pair, "Name", "Value");
        }
    }

    private static JsonElement.ArrayEnumerator BoundArray(JsonElement owner, string name, ref int total)
    {
        var value = owner.GetProperty(name);
        Require(value.ValueKind == JsonValueKind.Array, "An array is missing or exceeds the resource bound.");
        var count = value.GetArrayLength();
        Require(count <= MaximumArrayItems, "An array exceeds the resource bound.");
        try
        {
            total = checked(total + count);
        }
        catch (OverflowException)
        {
            throw new InvalidDataException("The membership count exceeds the resource bound.");
        }

        Require(total <= MaximumMemberships, "The membership count exceeds the resource bound.");
        return value.EnumerateArray();
    }

    private static void ValidateSnapshot(WorkspaceSnapshot snapshot)
    {
        Require(snapshot is not null, "The snapshot is null.");
        Require(
            snapshot.SchemaVersion == OrdinaryWorkspaceCapture.SchemaVersion && snapshot.Profile == OrdinaryWorkspaceCapture.Profile,
            "The snapshot schema is unsupported.");
        var budget = new TextBudget();
        budget.Add(snapshot.Profile);
        budget.Add(snapshot.RoslynVersion);
        budget.Add(snapshot.WorkspacePath);
        Require(Version.TryParse(snapshot.RoslynVersion, out _), "The Roslyn version is invalid.");
        ValidateCanonicalPath(snapshot.WorkspacePath, nullable: false);
        Require(snapshot.Properties is not null, "The load properties are null.");
        ValidatePropertySet(snapshot.Properties.RawRequested, ref budget);
        ValidatePropertySet(snapshot.Properties.LoaderGlobals, ref budget);
        var total = 0;
        AddArray(snapshot.Projects, ref total);
        AddArray(snapshot.InputPaths, ref total);
        var keys = new Dictionary<SnapshotProjectKey, SnapshotProjectKey>(ProjectKeyComparer.Instance);
        foreach (var project in snapshot.Projects)
        {
            Require(project is not null && project.Key is not null, "A project is null.");
            ValidateKey(project.Key, ref budget);
            Require(keys.TryAdd(project.Key, project.Key), "Duplicate project instances are forbidden.");
        }

        foreach (var project in snapshot.Projects)
        {
            ValidateProject(project, keys, ref total, ref budget);
        }

        var paths = new HashSet<string>(InputPathCanon.Comparer);
        foreach (var input in snapshot.InputPaths)
        {
            ValidateInputPath(input, keys, paths, ref total, ref budget);
        }

        ValidateGraph(snapshot.Projects, snapshot.InputPaths);
    }

    private static void ValidateProject(
        SnapshotProject project,
        Dictionary<SnapshotProjectKey, SnapshotProjectKey> keys,
        ref int total,
        ref TextBudget budget)
    {
        ValidateEvaluated(project.EvaluatedProperties, project.Key, ref total, ref budget);
        budget.Add(project.Name);
        budget.Add(project.AssemblyName);
        budget.Add(project.Language);
        Require(project.Name.Length > 0 && project.AssemblyName.Length > 0, "A project name is required.");
        Require(project.Language == LanguageNames.CSharp, "The language is unsupported.");
        ValidateOptionalPath(project.OutputFilePath, ref budget);
        ValidateOptionalPath(project.OutputRefFilePath, ref budget);
        budget.Add(project.DefaultNamespace);
        ValidateOptionalPath(project.CompilationAssemblyPath, ref budget);
        ValidateOptionalPath(project.GeneratedFilesOutputDirectory, ref budget);
        ValidateParse(project.ParseOptions, ref total, ref budget);
        ValidateCompilation(project.CompilationOptions, ref total, ref budget);
        AddArray(project.Documents, ref total);
        var memberships = new HashSet<DocumentMembership>(DocumentMembershipComparer.Instance);
        foreach (var document in project.Documents)
        {
            ValidateDocument(document, memberships, ref total, ref budget);
        }

        AddArray(project.ProjectReferences, ref total);
        var edges = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in project.ProjectReferences)
        {
            ValidateProjectReference(reference, keys, edges, ref total, ref budget);
        }

        AddArray(project.MetadataReferences, ref total);
        var metadata = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in project.MetadataReferences)
        {
            ValidateMetadata(reference, metadata, ref total, ref budget);
        }

        AddArray(project.AnalyzerReferences, ref total);
        var analyzers = new HashSet<string>(InputPathCanon.Comparer);
        foreach (var reference in project.AnalyzerReferences)
        {
            ValidateAnalyzer(reference, analyzers, ref budget);
        }
    }

    private static void ValidateKey(SnapshotProjectKey key, ref TextBudget budget)
    {
        ValidateCanonicalPath(key.ProjectPath, nullable: false);
        budget.Add(key.ProjectPath);
        budget.Add(key.Configuration);
        budget.Add(key.Platform);
        budget.Add(key.TargetFramework);
        Require(key.Configuration == "Debug", "An option value is invalid.");
        Require(key.Platform.Length > 0 && key.TargetFramework.Length > 0, "An option value is invalid.");
    }

    private static void ValidateEvaluated(
        SnapshotEvaluatedProperties evaluated,
        SnapshotProjectKey key,
        ref int total,
        ref TextBudget budget)
    {
        Require(evaluated is not null && evaluated.Values is not null, "Evaluated properties are null.");
        Require(evaluated.Source == OrdinaryWorkspaceCapture.EvaluatedPropertySource, "An option value is invalid.");
        budget.Add(evaluated.Source);
        Require(
            evaluated.Values.Configuration == key.Configuration
            && evaluated.Values.Platform == key.Platform
            && evaluated.Values.TargetFramework == key.TargetFramework,
            "An option value is invalid.");
        budget.Add(evaluated.Values.Configuration);
        budget.Add(evaluated.Values.Platform);
        budget.Add(evaluated.Values.TargetFramework);
        AddArray(evaluated.UnknownProperties, ref total);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in evaluated.UnknownProperties)
        {
            Require(name is not null && name.Length > 0, "An option value is invalid.");
            budget.Add(name);
            Require(name is not ("Configuration" or "Platform" or "TargetFramework"), "An option value is invalid.");
            Require(seen.Add(name), "An option map contains a duplicate or unsorted key.");
        }
    }

    private static void ValidatePropertySet(SnapshotPropertySet set, ref TextBudget budget)
    {
        Require(set is not null, "A property set is null.");
        budget.Add(set.Configuration);
        budget.Add(set.Platform);
        budget.Add(set.TargetFramework);
    }

    private static void ValidateParse(SnapshotParseOptions options, ref int total, ref TextBudget budget)
    {
        Require(options is not null, "Parse options are null.");
        RequireEnumName<LanguageVersion>(options.SpecifiedLanguageVersion);
        RequireEnumName<LanguageVersion>(options.LanguageVersion);
        RequireEnumName<DocumentationMode>(options.DocumentationMode);
        RequireEnumName<SourceCodeKind>(options.Kind);
        budget.Add(options.SpecifiedLanguageVersion);
        budget.Add(options.LanguageVersion);
        budget.Add(options.DocumentationMode);
        budget.Add(options.Kind);
        ValidateStringList(options.PreprocessorSymbols, ref total, ref budget);
        ValidateMap(options.Features, ref total, ref budget, diagnostic: false);
        RequireRepresentableParseOptions(options);
    }

    private static void RequireRepresentableParseOptions(SnapshotParseOptions options)
    {
        // Default and Latest are accepted only when the payload names the effective version
        // produced by the public constructor. Comparing the two DTO strings would reject that
        // normalization, or accept a version the constructor does not produce.
        // The same constructor reports preprocessor symbols it will not keep.
        CSharpParseOptions reconstructed;
        try
        {
            reconstructed = new CSharpParseOptions(
                    Enum.Parse<LanguageVersion>(options.SpecifiedLanguageVersion),
                    Enum.Parse<DocumentationMode>(options.DocumentationMode),
                    Enum.Parse<SourceCodeKind>(options.Kind),
                    options.PreprocessorSymbols)
                .WithFeatures(options.Features.Select(pair => new KeyValuePair<string, string>(pair.Name, pair.Value)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("An option value is invalid.", exception);
        }

        Require(reconstructed.LanguageVersion.ToString() == options.LanguageVersion, "An option value is invalid.");
        Require(!reconstructed.Errors.Any(), "An option value is invalid.");
    }

    private static void ValidateCompilation(SnapshotCompilationOptions options, ref int total, ref TextBudget budget)
    {
        Require(options is not null, "Compilation options are null.");
        Require(options.ConstructionWitness == "csharp-command-line-debug-plus-v1", "An option value is invalid.");
        RequireEnumName<OutputKind>(options.OutputKind);
        RequireEnumName<OptimizationLevel>(options.OptimizationLevel);
        RequireEnumName<Platform>(options.Platform);
        RequireEnumName<ReportDiagnostic>(options.GeneralDiagnosticOption);
        RequireEnumName<MetadataImportOptions>(options.MetadataImportOptions);
        RequireEnumName<NullableContextOptions>(options.NullableContextOptions);
        Require(options.WarningLevel is >= 0 and <= 65535, "An option value is invalid.");
        Require(options.AssemblyIdentityComparer is "desktop-default" or "default", "An option value is invalid.");
        Require(options.StrongNamePolicy == SnapshotSigningProfile.IndependentPolicy, "An option value is invalid.");
        Require(
            options.SyntaxTreeOptionsPolicy is "none" or "public-descriptor-null-provider-fresh-project-analyzer-configs-v1",
            "An option value is invalid.");
        budget.Add(options.ConstructionWitness);
        budget.Add(options.OutputKind);
        budget.Add(options.ModuleName);
        budget.Add(options.MainTypeName);
        budget.Add(options.ScriptClassName);
        budget.Add(options.OptimizationLevel);
        budget.Add(options.CryptoKeyContainer);
        budget.Add(options.CryptoKeyFile);
        budget.Add(options.Platform);
        budget.Add(options.GeneralDiagnosticOption);
        budget.Add(options.MetadataImportOptions);
        budget.Add(options.NullableContextOptions);
        budget.Add(options.AssemblyIdentityComparer);
        budget.Add(options.StrongNamePolicy);
        budget.Add(options.SyntaxTreeOptionsPolicy);
        Require(options.CryptoPublicKey is not null, "A byte array is required.");
        Require(options.CryptoPublicKey.Length <= 4096, "A byte array exceeds the resource bound.");
        budget.AddBytes(options.CryptoPublicKey);
        ValidateStringList(options.Usings, ref total, ref budget);
        ValidateMap(options.SpecificDiagnosticOptions, ref total, ref budget, diagnostic: true);
        ValidateResolver(options.SourceResolver, "source", ref total, ref budget);
        ValidateResolver(options.XmlResolver, "xml", ref total, ref budget);
        ValidateResolver(options.MetadataResolver, "metadata", ref total, ref budget);
    }

    private static void ValidateResolver(SnapshotResolver resolver, string role, ref int total, ref TextBudget budget)
    {
        Require(resolver is not null, "A resolver descriptor is required.");
        budget.Add(resolver.Kind);
        switch (role, resolver.Kind)
        {
            case ("source", "none"):
            case ("xml", "none"):
                Require(resolver.BaseDirectory is null, "The public option service is unsupported.");
                Require(resolver.SearchPaths is { Length: 0 } && resolver.PathMap is { Length: 0 }, "The public option service is unsupported.");
                AddArray(resolver.SearchPaths, ref total);
                AddArray(resolver.PathMap, ref total);
                break;
            case ("source", "source-file-v1"):
                ValidateOptionalPath(resolver.BaseDirectory, ref budget);
                ValidateStringList(resolver.SearchPaths, ref total, ref budget);
                ValidateMap(resolver.PathMap, ref total, ref budget, diagnostic: false);
                break;
            case ("xml", "xml-file-v1"):
                ValidateOptionalPath(resolver.BaseDirectory, ref budget);
                Require(resolver.SearchPaths is { Length: 0 } && resolver.PathMap is { Length: 0 }, "The public option service is unsupported.");
                AddArray(resolver.SearchPaths, ref total);
                AddArray(resolver.PathMap, ref total);
                break;
            case ("metadata", "command-line-project-factory-v1"):
                ValidateCanonicalPath(resolver.BaseDirectory, nullable: false);
                budget.Add(resolver.BaseDirectory);
                Require(resolver.SearchPaths is { Length: 0 } && resolver.PathMap is { Length: 0 }, "The public option service is unsupported.");
                AddArray(resolver.SearchPaths, ref total);
                AddArray(resolver.PathMap, ref total);
                break;
            default:
                throw new InvalidDataException("The public option service is unsupported.");
        }
    }

    private static void ValidateDocument(
        SnapshotDocument document,
        HashSet<DocumentMembership> memberships,
        ref int total,
        ref TextBudget budget)
    {
        Require(document is not null, "A document is null.");
        Require(document.Kind is "source" or "additional" or "analyzer-config", "The document kind is unsupported.");
        Require(!document.IsGenerated || document.Kind == "source", "The document kind is unsupported.");
        ValidateCanonicalPath(document.Path, nullable: false);
        budget.Add(document.Kind);
        budget.Add(document.Path);
        budget.Add(document.Name);
        budget.Add(document.SourceCodeKind);
        Require(document.Name.Length > 0, "A document name is required.");
        RequireEnumName<SourceCodeKind>(document.SourceCodeKind);
        Require(memberships.Add(new DocumentMembership(document.Kind, document.Path)), "A document membership is duplicated.");
        ValidateStringList(document.Folders, ref total, ref budget);
        ValidateText(document.TextMetadata, ref budget);
    }

    private static void ValidateText(SnapshotTextMetadata metadata, ref TextBudget budget)
    {
        Require(metadata is not null, "Document text metadata is null.");
        Require(metadata.EncodingPreamble is not null && metadata.Checksum is not null, "A byte array is required.");
        Require(metadata.EncodingPreamble.Length <= 16, "The encoding preamble exceeds the bound.");
        budget.AddBytes(metadata.EncodingPreamble);
        if (metadata.EncodingCodePage is null)
        {
            Require(metadata.EncodingPreamble.Length == 0, "Unspecified encoding carries a preamble.");
        }
        else
        {
            Require(TryGetEncoding(metadata.EncodingCodePage.Value), "The encoding code page is invalid.");
        }

        var expected = metadata.ChecksumAlgorithm switch
        {
            "Sha1" => 20,
            "Sha256" => 32,
            _ => -1,
        };
        Require(expected > 0 && metadata.Checksum.Length == expected, "A document checksum does not match its algorithm.");
        budget.Add(metadata.ChecksumAlgorithm);
        budget.AddBytes(metadata.Checksum);
    }

    private static bool TryGetEncoding(int codePage)
    {
        try
        {
            _ = Encoding.GetEncoding(codePage);
            return codePage > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void ValidateProjectReference(
        SnapshotProjectReference reference,
        Dictionary<SnapshotProjectKey, SnapshotProjectKey> keys,
        HashSet<string> edges,
        ref int total,
        ref TextBudget budget)
    {
        Require(reference is not null && reference.Target is not null, "A project reference is null.");
        Require(keys.ContainsKey(reference.Target), "A project reference is unbound.");
        var target = keys[reference.Target];
        ValidateAliases(reference.Aliases, ref total, ref budget);
        var identity = string.Join(
            '\n',
            PathKey(target.ProjectPath),
            target.Configuration,
            target.Platform,
            target.TargetFramework,
            string.Join('\0', reference.Aliases),
            reference.EmbedInteropTypes);
        Require(edges.Add(identity), "A project reference is duplicated.");
    }

    private static void ValidateMetadata(
        SnapshotMetadataReference reference,
        HashSet<string> seen,
        ref int total,
        ref TextBudget budget)
    {
        Require(reference is not null, "A metadata reference is null.");
        ValidateCanonicalPath(reference.Path, nullable: false);
        budget.Add(reference.Path);
        budget.Add(reference.Kind);
        budget.Add(reference.BinaryDocumentationPolicy);
        RequireEnumName<MetadataImageKind>(reference.Kind);
        Require(reference.BinaryDocumentationPolicy == OrdinaryWorkspaceCapture.BinaryDocumentationPolicy, "An option value is invalid.");
        ValidateAliases(reference.Aliases, ref total, ref budget);
        var identity = string.Join(
            '\n',
            PathKey(reference.Path),
            reference.Kind,
            string.Join('\0', reference.Aliases),
            reference.EmbedInteropTypes,
            reference.BinaryDocumentationPolicy);
        Require(seen.Add(identity), "A metadata reference is duplicated.");
    }

    private static void ValidateAnalyzer(SnapshotAnalyzerReference reference, HashSet<string> seen, ref TextBudget budget)
    {
        Require(reference is not null, "An analyzer reference is null.");
        ValidateCanonicalPath(reference.OriginalPath, nullable: false);
        budget.Add(reference.OriginalPath);
        budget.Add(reference.IdentitySource);
        Require(reference.IdentitySource == OrdinaryWorkspaceCapture.AnalyzerIdentitySource, "An option value is invalid.");
        Require(!HasPathSegment(reference.OriginalPath, "RoslynMcpServer.AnalyzerShadowCopy"), "An analyzer identity uses a shadow path.");
        Require(seen.Add(reference.OriginalPath), "An analyzer reference is duplicated.");
    }

    private static void ValidateAliases(string[] aliases, ref int total, ref TextBudget budget)
    {
        AddArray(aliases, ref total);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alias in aliases)
        {
            Require(alias is not null && alias.Length > 0, "An alias is duplicated or empty.");
            budget.Add(alias);
            Require(seen.Add(alias), "An alias is duplicated or empty.");
        }
    }

    private static void ValidateInputPath(
        SnapshotInputPath input,
        Dictionary<SnapshotProjectKey, SnapshotProjectKey> keys,
        HashSet<string> paths,
        ref int total,
        ref TextBudget budget)
    {
        Require(input is not null, "An input path is null.");
        ValidateCanonicalPath(input.Path, nullable: false);
        budget.Add(input.Path);
        Require(paths.Add(input.Path), "An input path is duplicated.");
        AddArray(input.Occurrences, ref total);
        var occurrences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var occurrence in input.Occurrences)
        {
            ValidateOccurrence(occurrence, keys, occurrences, ref budget);
        }
    }

    private static void ValidateOccurrence(
        SnapshotInputOccurrence occurrence,
        Dictionary<SnapshotProjectKey, SnapshotProjectKey> keys,
        HashSet<string> occurrences,
        ref TextBudget budget)
    {
        Require(occurrence is not null && occurrence.Owner is not null, "An input occurrence is null.");
        Require(keys.ContainsKey(occurrence.Owner), "An input occurrence owner is unbound.");
        var owner = keys[occurrence.Owner];
        budget.Add(occurrence.DocumentKind);
        budget.Add(occurrence.Role);
        budget.Add(occurrence.RoleSource);
        budget.Add(occurrence.ProducerSource);
        switch (occurrence.Role)
        {
            case "UserInput":
            case "Unknown":
                RequireDocumentRole(occurrence, "source", allowUnprovenGeneratedFlag: true);
                Require(occurrence.Producer is null && occurrence.ProducerSource is null, "An option value is invalid.");
                break;
            case "Generated":
                RequireDocumentRole(occurrence, "source", allowUnprovenGeneratedFlag: false);
                Require(occurrence.RoleSource == SnapshotOccurrenceBinder.DocumentRoleSource, "An option value is invalid.");
                Require(ProducerIs(occurrence.Producer, owner, keys), "An option value is invalid.");
                Require(occurrence.ProducerSource == SnapshotOccurrenceBinder.GeneratedProducerSource, "An option value is invalid.");
                break;
            case "AdditionalFile":
                RequireDocumentRole(occurrence, "additional", allowUnprovenGeneratedFlag: true);
                Require(occurrence.Producer is null && occurrence.ProducerSource is null, "An option value is invalid.");
                break;
            case "AnalyzerConfig":
                RequireDocumentRole(occurrence, "analyzer-config", allowUnprovenGeneratedFlag: true);
                Require(occurrence.Producer is null && occurrence.ProducerSource is null, "An option value is invalid.");
                break;
            case "EvaluationInput":
                Require(occurrence.DocumentKind is null, "The input role is unsupported.");
                Require(occurrence.RoleSource == SnapshotOccurrenceBinder.ProjectFileRoleSource, "An option value is invalid.");
                Require(occurrence.Producer is null && occurrence.ProducerSource is null, "An option value is invalid.");
                break;
            case "Output":
                Require(occurrence.DocumentKind is null, "The input role is unsupported.");
                Require(occurrence.RoleSource == SnapshotOccurrenceBinder.OutputRoleSource, "An option value is invalid.");
                Require(ProducerIs(occurrence.Producer, owner, keys), "An option value is invalid.");
                Require(occurrence.ProducerSource == SnapshotOccurrenceBinder.OutputRoleSource, "An option value is invalid.");
                break;
            case "MetadataDependency":
                Require(occurrence.DocumentKind is null, "The input role is unsupported.");
                Require(occurrence.RoleSource == SnapshotOccurrenceBinder.MetadataRoleSource, "An option value is invalid.");
                if (occurrence.Producer is null)
                {
                    Require(occurrence.ProducerSource is null, "An option value is invalid.");
                }
                else
                {
                    Require(keys.ContainsKey(occurrence.Producer), "An input occurrence producer is unbound.");
                    Require(occurrence.ProducerSource == SnapshotOccurrenceBinder.MetadataProducerSource, "An option value is invalid.");
                }

                break;
            case "AnalyzerDependency":
                Require(occurrence.DocumentKind is null, "The input role is unsupported.");
                Require(occurrence.RoleSource == OrdinaryWorkspaceCapture.AnalyzerIdentitySource, "An option value is invalid.");
                Require(occurrence.Producer is null && occurrence.ProducerSource is null, "An option value is invalid.");
                break;
            default:
                throw new InvalidDataException("The input role is unsupported.");
        }

        var identity = string.Join(
            '\n',
            PathKey(owner.ProjectPath),
            owner.Configuration,
            owner.Platform,
            owner.TargetFramework,
            occurrence.Role,
            occurrence.DocumentKind ?? string.Empty);
        Require(occurrences.Add(identity), "An input occurrence is duplicated.");
    }

    private static void ValidateGraph(SnapshotProject[] projects, SnapshotInputPath[] inputPaths)
    {
        // Membership and producer proof use only the DTO. This does not read the disk.
        var slots = MembershipSlots(projects);
        foreach (var input in inputPaths)
        {
            foreach (var occurrence in input.Occurrences)
            {
                var slot = FindOpenSlot(slots, input.Path, occurrence);
                Require(slot is not null, "An input occurrence does not match a project membership.");
                slot!.Consumed = true;
                if (occurrence.Role == "MetadataDependency")
                {
                    ValidateMetadataProducer(projects, occurrence, input.Path);
                }
            }
        }

        Require(slots.TrueForAll(static slot => slot.Consumed), "A project membership has no input occurrence.");
    }

    private static List<MembershipSlot> MembershipSlots(SnapshotProject[] projects)
    {
        var slots = new List<MembershipSlot>();
        foreach (var project in projects)
        {
            AddSlot(slots, project.Key, project.Key.ProjectPath, null, "EvaluationInput");
            foreach (var document in project.Documents)
            {
                if (document.Kind == "source" && document.IsGenerated)
                {
                    AddSlot(slots, project.Key, document.Path, document.Kind, "Generated");
                }
                else if (document.Kind == "source")
                {
                    AddSlot(slots, project.Key, document.Path, document.Kind, "UserInput", "Unknown");
                }
                else if (document.Kind == "additional")
                {
                    AddSlot(slots, project.Key, document.Path, document.Kind, "AdditionalFile");
                }
                else
                {
                    AddSlot(slots, project.Key, document.Path, document.Kind, "AnalyzerConfig");
                }
            }

            AddOutputSlot(slots, project, project.OutputFilePath);
            AddOutputSlot(slots, project, project.OutputRefFilePath);
            foreach (var reference in project.MetadataReferences)
            {
                AddSlot(slots, project.Key, reference.Path, null, "MetadataDependency");
            }

            foreach (var analyzer in project.AnalyzerReferences)
            {
                AddSlot(slots, project.Key, analyzer.OriginalPath, null, "AnalyzerDependency");
            }
        }

        return slots;
    }

    private static void AddOutputSlot(List<MembershipSlot> slots, SnapshotProject project, string? path)
    {
        if (path is null)
        {
            return;
        }

        AddSlot(slots, project.Key, path, null, "Output");
    }

    private static void AddSlot(
        List<MembershipSlot> slots,
        SnapshotProjectKey owner,
        string path,
        string? documentKind,
        params string[] roles)
    {
        if (FindSlot(slots, owner, path, documentKind, roles[0]) is not null)
        {
            return;
        }

        slots.Add(new MembershipSlot(owner, path, documentKind, roles));
    }

    private static MembershipSlot? FindOpenSlot(
        List<MembershipSlot> slots,
        string path,
        SnapshotInputOccurrence occurrence) =>
        FindSlot(slots, occurrence.Owner, path, occurrence.DocumentKind, occurrence.Role, openOnly: true);

    private static MembershipSlot? FindSlot(
        List<MembershipSlot> slots,
        SnapshotProjectKey owner,
        string path,
        string? documentKind,
        string role,
        bool openOnly = false)
    {
        foreach (var slot in slots)
        {
            if (openOnly && slot.Consumed)
            {
                continue;
            }

            if (ProjectKeyComparer.Instance.Equals(slot.Owner, owner)
                && InputPathCanon.Comparer.Equals(slot.Path, path)
                && slot.DocumentKind == documentKind
                && Array.IndexOf(slot.Roles, role) >= 0)
            {
                return slot;
            }
        }

        return null;
    }

    private static void ValidateMetadataProducer(
        SnapshotProject[] projects,
        SnapshotInputOccurrence occurrence,
        string path)
    {
        SnapshotProject? consumer = null;
        var owners = new List<SnapshotProjectKey>();
        foreach (var project in projects)
        {
            if (ProjectKeyComparer.Instance.Equals(project.Key, occurrence.Owner))
            {
                consumer = project;
            }

            if (SamePath(project.OutputFilePath, path) || SamePath(project.OutputRefFilePath, path))
            {
                if (!owners.Exists(owner => ProjectKeyComparer.Instance.Equals(owner, project.Key)))
                {
                    owners.Add(project.Key);
                }
            }
        }

        SnapshotProjectKey? proven = null;
        if (owners.Count == 1
            && consumer is not null
            && consumer.ProjectReferences.Any(edge => ProjectKeyComparer.Instance.Equals(edge.Target, owners[0])))
        {
            proven = owners[0];
        }

        var matches = proven is not null
            && occurrence.Producer is not null
            && ProjectKeyComparer.Instance.Equals(occurrence.Producer, proven)
            && occurrence.ProducerSource == SnapshotOccurrenceBinder.MetadataProducerSource;
        var absent = proven is null && occurrence.Producer is null && occurrence.ProducerSource is null;
        Require(matches || absent, "A metadata producer is not proven by a unique output and a directed project reference.");
    }

    private static bool SamePath(string? left, string? right) =>
        left is not null && right is not null && InputPathCanon.Comparer.Equals(left, right);

    private static bool ProducerIs(
        SnapshotProjectKey? producer,
        SnapshotProjectKey owner,
        Dictionary<SnapshotProjectKey, SnapshotProjectKey> keys) =>
        producer is not null
        && keys.TryGetValue(producer, out var stored)
        && ProjectKeyComparer.Instance.Equals(stored, owner);

    private static void RequireDocumentRole(SnapshotInputOccurrence occurrence, string kind, bool allowUnprovenGeneratedFlag)
    {
        Require(occurrence.DocumentKind == kind, "The input role is unsupported.");
        var source = occurrence.RoleSource;
        Require(
            source == SnapshotOccurrenceBinder.DocumentRoleSource
            || (allowUnprovenGeneratedFlag && source == SnapshotOccurrenceBinder.UnavailableGeneratedFlagSource),
            "An option value is invalid.");
    }

    private static void ValidateMap(SnapshotNameValue[] pairs, ref int total, ref TextBudget budget, bool diagnostic)
    {
        AddArray(pairs, ref total);
        string? previous = null;
        foreach (var pair in pairs)
        {
            Require(pair is not null, "An option map entry is null.");
            budget.Add(pair.Name);
            budget.Add(pair.Value);
            Require(pair.Name.Length > 0, "An option map entry is invalid.");
            Require(
                previous is null || StringComparer.Ordinal.Compare(previous, pair.Name) < 0,
                "An option map contains a duplicate or unsorted key.");
            if (diagnostic)
            {
                RequireEnumName<ReportDiagnostic>(pair.Value);
            }

            previous = pair.Name;
        }
    }

    private static void ValidateStringList(string[] values, ref int total, ref TextBudget budget)
    {
        AddArray(values, ref total);
        foreach (var value in values)
        {
            Require(value is not null, "A string array member is invalid.");
            budget.Add(value);
        }
    }

    private static void ValidateOptionalPath(string? path, ref TextBudget budget)
    {
        if (path is null)
        {
            return;
        }

        ValidateCanonicalPath(path, nullable: false);
        budget.Add(path);
    }

    private static void ValidateCanonicalPath(string? path, bool nullable)
    {
        if (path is null)
        {
            Require(nullable, "A canonical absolute path is required.");
            return;
        }

        var canonical = InputPathCanon.TryCanonicalize(path);
        Require(
            path.Length <= MaximumStringLength
            && path.IndexOf('\0') < 0
            && canonical is not null
            && path == canonical
            && Path.IsPathFullyQualified(path),
            "A canonical absolute path is required.");
    }

    private static void AddArray<T>(T[]? values, ref int total)
    {
        Require(values is not null, "An array is missing or exceeds the resource bound.");
        Require(values.Length <= MaximumArrayItems, "An array exceeds the resource bound.");
        try
        {
            total = checked(total + values.Length);
        }
        catch (OverflowException)
        {
            throw new InvalidDataException("The membership count exceeds the resource bound.");
        }

        Require(total <= MaximumMemberships, "The membership count exceeds the resource bound.");
    }

    private static byte[] SerializePayload(WorkspaceSnapshot snapshot)
    {
        using var stream = new BoundedPayloadStream();
        JsonSerializer.Serialize(stream, snapshot, SerializerOptions);
        return stream.ToArray();
    }

    private static byte[] BuildEnvelope(byte[] payload)
    {
        var checksum = Convert.ToHexString(SHA256.HashData(payload));
        var prefix = Encoding.UTF8.GetBytes(EnvelopePrefix + checksum + EnvelopeMiddle);
        if (prefix.Length > MaximumBytes || payload.Length > MaximumBytes - prefix.Length - 1)
        {
            throw new InvalidDataException("The snapshot exceeds the byte bound.");
        }

        var envelope = new byte[prefix.Length + payload.Length + 1];
        prefix.CopyTo(envelope, 0);
        payload.CopyTo(envelope, prefix.Length);
        envelope[^1] = (byte)'}';
        return envelope;
    }

    private static void RequireShape(JsonElement element, params string[] keys)
    {
        Require(element.ValueKind == JsonValueKind.Object, "An object is required.");
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            Require(actual.Add(property.Name), "An object contains a duplicate key.");
        }

        Require(actual.Count == keys.Length, "An object contains missing or extra keys.");
        foreach (var key in keys)
        {
            Require(actual.Contains(key), "An object contains missing or extra keys.");
        }
    }

    private static void RequireEnumName<TEnum>(string value)
        where TEnum : struct, Enum
    {
        Require(
            Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed) && parsed.ToString() == value,
            "An enum option is invalid.");
    }

    private static bool HasPathSegment(string path, string segment)
    {
        foreach (var part in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Equals(segment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string PathKey(string path) => OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;

    private static void Require([DoesNotReturnIf(false)] bool condition, string reason)
    {
        if (!condition)
        {
            throw new InvalidDataException(reason);
        }
    }

    private static bool IsMalformed(Exception exception) =>
        exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException
            or FormatException or KeyNotFoundException or NullReferenceException;

    private struct TextBudget
    {
        public void Add(string? value)
        {
            if (value is null)
            {
                return;
            }

            if (value.Length > MaximumStringLength)
            {
                throw new InvalidDataException("A string exceeds the resource bound.");
            }

            _characters += value.Length;
            if (_characters > MaximumBytes)
            {
                throw new InvalidDataException("The snapshot exceeds the byte bound.");
            }
        }

        public void AddBytes(byte[] value)
        {
            _characters += value.Length;
            if (_characters > MaximumBytes)
            {
                throw new InvalidDataException("The snapshot exceeds the byte bound.");
            }
        }

        private long _characters;
    }

    private sealed class MembershipSlot
    {
        public SnapshotProjectKey Owner { get; }

        public string Path { get; }

        public string? DocumentKind { get; }

        public string[] Roles { get; }

        public bool Consumed { get; set; }

        public MembershipSlot(SnapshotProjectKey owner, string path, string? documentKind, string[] roles)
        {
            Owner = owner;
            Path = path;
            DocumentKind = documentKind;
            Roles = roles;
        }
    }

    private readonly record struct DocumentMembership(string Kind, string Path);

    private sealed class DocumentMembershipComparer : IEqualityComparer<DocumentMembership>
    {
        public static DocumentMembershipComparer Instance { get; } = new();

        public bool Equals(DocumentMembership left, DocumentMembership right) =>
            left.Kind == right.Kind && InputPathCanon.Comparer.Equals(left.Path, right.Path);

        public int GetHashCode(DocumentMembership value) =>
            HashCode.Combine(value.Kind, InputPathCanon.Comparer.GetHashCode(value.Path));
    }

    private sealed class ProjectKeyComparer : IEqualityComparer<SnapshotProjectKey>
    {
        public static ProjectKeyComparer Instance { get; } = new();

        public bool Equals(SnapshotProjectKey? left, SnapshotProjectKey? right) =>
            left is not null
            && right is not null
            && InputPathCanon.Comparer.Equals(left.ProjectPath, right.ProjectPath)
            && left.Configuration == right.Configuration
            && left.Platform == right.Platform
            && left.TargetFramework == right.TargetFramework;

        public int GetHashCode(SnapshotProjectKey key) =>
            HashCode.Combine(
                InputPathCanon.Comparer.GetHashCode(key.ProjectPath),
                key.Configuration,
                key.Platform,
                key.TargetFramework);
    }

    private sealed class BoundedPayloadStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckBound(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            CheckBound(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            CheckBound(1);
            base.WriteByte(value);
        }

        private void CheckBound(int count)
        {
            if (count < 0 || Position > PayloadByteLimit - count)
            {
                throw new InvalidDataException("The snapshot exceeds the byte bound.");
            }
        }
    }

    private static readonly string EnvelopePrefix =
        "{\"Schema\":\"" + EnvelopeSchema + "\",\"ChecksumAlgorithm\":\"" + ChecksumAlgorithmName + "\",\"Checksum\":\"";

    private const string EnvelopeMiddle = "\",\"Payload\":";

    private static readonly int EnvelopeOverhead =
        Encoding.UTF8.GetByteCount(EnvelopePrefix) + 64 + Encoding.UTF8.GetByteCount(EnvelopeMiddle) + 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 64,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        NumberHandling = JsonNumberHandling.Strict,
        AllowDuplicateProperties = false,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        MaxDepth = 64,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        MaxDepth = 64,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowMultipleValues = false,
    };
}
