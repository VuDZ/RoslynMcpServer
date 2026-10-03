using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal static class SemanticSnapshotCodec
{
    public static byte[] Encode(SemanticSnapshot snapshot)
    {
        try
        {
            ValidateSnapshot(snapshot);
            using var stream = new BoundedPayloadStream();
            JsonSerializer.Serialize(stream, snapshot, SerializerOptions);
            var payload = stream.ToArray();
            _ = Decode(payload);
            return payload;
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            throw new InvalidDataException("The semantic snapshot is not representable.", exception);
        }
    }

    public static SemanticSnapshot Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaximumBytes)
        {
            throw new InvalidDataException("The semantic snapshot exceeds the byte bound.");
        }
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            ValidateJson(document.RootElement);
            ValidateSnapshotShape(document.RootElement);
            var snapshot = document.RootElement.Deserialize<SemanticSnapshot>(SerializerOptions)
                ?? throw new InvalidDataException("The semantic snapshot is null.");
            ValidateSnapshot(snapshot);
            return snapshot;
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            throw new InvalidDataException("The semantic snapshot is malformed.", exception);
        }
    }

    private static void ValidateSnapshotShape(JsonElement snapshot)
    {
        RequireShape(snapshot, "Schema", "Request", "Projects", "ProjectReferences");
        var request = snapshot.GetProperty("Request");
        RequireShape(request, "WorkspacePath", "Configuration", "Platform", "TargetFramework", "SelectedRoots", "Scope", "MetadataMode");
        foreach (var project in Array(snapshot, "Projects", 4096))
        {
            RequireShape(project, "Key", "Name", "AssemblyName", "Language", "OutputPath", "OutputReferencePath",
                "ParseOptions", "CompilationOptions", "Documents", "MetadataReferences", "Analyzers");
            ValidateKeyShape(project.GetProperty("Key"));
            foreach (var entry in Array(project, "Documents", MaximumMemberships))
            {
                RequireShape(entry, "Project", "Role", "Path", "Name", "Folders", "SourceKind", "GeneratedOnDisk", "Encoding");
                ValidateKeyShape(entry.GetProperty("Project"));
                RequireEnum<SourceCodeKind>(entry.GetProperty("SourceKind"));
                RequireShape(entry.GetProperty("Encoding"), "CodePage", "Preamble", "DecoderFallback", "DecoderReplacement",
                    "EncoderFallback", "EncoderReplacement");
            }
            foreach (var entry in Array(project, "MetadataReferences", MaximumMemberships))
            {
                RequireShape(entry, "Path", "Kind", "Aliases", "EmbedInteropTypes", "DocumentationPath");
                RequireEnum<MetadataImageKind>(entry.GetProperty("Kind"));
            }
            foreach (var entry in Array(project, "Analyzers", MaximumMemberships))
            {
                RequireShape(entry, "Consumer", "OriginalPath", "Sha256", "OriginDirectory", "DependencyPaths");
                ValidateKeyShape(entry.GetProperty("Consumer"));
            }
        }
        foreach (var edge in Array(snapshot, "ProjectReferences", MaximumMemberships))
        {
            RequireShape(edge, "From", "To", "Aliases", "EmbedInteropTypes");
            ValidateKeyShape(edge.GetProperty("From"));
            ValidateKeyShape(edge.GetProperty("To"));
        }
    }

    private static void ValidateKeyShape(JsonElement key) =>
        RequireShape(key, "ProjectPath", "InnerTargetFramework", "EffectiveProperties");

    private static JsonElement.ArrayEnumerator Array(JsonElement owner, string name, int maximum)
    {
        var value = owner.GetProperty(name);
        Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= maximum,
            "An array is missing or exceeds the resource bound.");
        return value.EnumerateArray();
    }

    private static void RequireShape(JsonElement element, params string[] keys)
    {
        Require(element.ValueKind == JsonValueKind.Object, "An object is required.");
        var actual = element.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Require(actual.SetEquals(keys), "An object contains missing or extra keys.");
    }

    private static void ValidateJson(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                Require(property.Name.Length <= MaximumString && names.Add(property.Name), "An object contains a duplicate or oversized key.");
                ValidateJson(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            Require(value.GetArrayLength() <= MaximumMemberships, "An array exceeds the resource bound.");
            foreach (var item in value.EnumerateArray())
            {
                ValidateJson(item);
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            Require(value.GetString()!.Length <= MaximumString, "A string exceeds the resource bound.");
        }
    }

    private static void ValidateSnapshot(SemanticSnapshot snapshot)
    {
        Require(snapshot is not null && snapshot.Schema == "e0-semantic-1", "The schema is unsupported.");
        ValidateRequest(snapshot!.Request);
        ValidateArray(snapshot.Projects);
        ValidateArray(snapshot.ProjectReferences);
        CheckTextBudget(snapshot);
        Require(snapshot.Projects.Length <= 4096, "The project count exceeds the resource bound.");
        var keys = new HashSet<InstanceKey>();
        long memberships = snapshot.ProjectReferences.Length;
        foreach (var project in snapshot.Projects)
        {
            Require(project is not null, "A project is null.");
            ValidateKey(project!.Key);
            Require(keys.Add(project.Key), "Duplicate project instances are forbidden.");
            ValidateString(project.Name);
            ValidateString(project.AssemblyName);
            Require(project.Language == LanguageNames.CSharp, "The language is unsupported.");
            ValidatePath(project.OutputPath, nullable: true);
            ValidatePath(project.OutputReferencePath, nullable: true);
            ValidateOptions(project.ParseOptions, ParseKeys, parse: true);
            ValidateOptions(project.CompilationOptions, CompilationKeys, parse: false);
            ValidateProjectInputs(project);
            memberships += project.Documents.Length + project.MetadataReferences.Length + project.Analyzers.Length;
        }
        Require(memberships <= MaximumMemberships, "The membership count exceeds the resource bound.");
        var edges = new HashSet<(InstanceKey From, InstanceKey To, string Aliases, bool Embed)>();
        foreach (var edge in snapshot.ProjectReferences)
        {
            Require(edge is not null, "A project edge is null.");
            ValidateKey(edge!.From);
            ValidateKey(edge.To);
            Require(keys.Contains(edge.From) && keys.Contains(edge.To), "A project edge is unbound.");
            ValidateStrings(edge!.Aliases);
            Require(edges.Add((edge.From, edge.To, JsonSerializer.Serialize(edge.Aliases), edge.EmbedInteropTypes)),
                "A project edge is duplicated.");
        }
        foreach (var root in snapshot.Request.SelectedRoots)
        {
            Require(keys.Any(key => PathComparer.Equals(root, key.ProjectPath)), "A selected root is missing.");
        }
    }

    private static void ValidateRequest(ExperimentRequest request)
    {
        Require(request is not null, "The request is null.");
        ValidatePath(request!.WorkspacePath);
        ValidateString(request.Configuration, nullable: true);
        ValidateString(request.Platform, nullable: true);
        ValidateString(request.TargetFramework, nullable: true);
        ValidateArray(request.SelectedRoots);
        Require(request.Scope is "full" or "selected", "The scope is unsupported.");
        Require(request.Scope != "selected" || !request.SelectedRoots.IsEmpty, "Selected scope requires roots.");
        var roots = new HashSet<string>(PathComparer);
        string? previous = null;
        foreach (var root in request.SelectedRoots)
        {
            ValidatePath(root);
            Require(roots.Add(root), "A selected root is duplicated.");
            Require(previous is null || PathComparer.Compare(previous, root) < 0, "Selected roots are not sorted.");
            previous = root;
        }
    }

    private static void CheckTextBudget(SemanticSnapshot snapshot)
    {
        long characters = 0;
        foreach (var value in SnapshotStrings(snapshot))
        {
            characters += value?.Length ?? 0;
            // JSON uses at least one byte per UTF-16 code unit. This lower bound rejects
            // impossible payloads before allocating membership/edge identity strings.
            if (characters > MaximumBytes)
            {
                throw new InvalidDataException("The semantic snapshot exceeds the byte bound.");
            }
        }
    }

    private static IEnumerable<string?> SnapshotStrings(SemanticSnapshot snapshot)
    {
        yield return snapshot.Schema;
        yield return snapshot.Request.WorkspacePath;
        yield return snapshot.Request.Configuration;
        yield return snapshot.Request.Platform;
        yield return snapshot.Request.TargetFramework;
        yield return snapshot.Request.Scope;
        foreach (var root in snapshot.Request.SelectedRoots)
        {
            yield return root;
        }
        foreach (var project in snapshot.Projects)
        {
            yield return project.Name;
            yield return project.AssemblyName;
            yield return project.Language;
            yield return project.OutputPath;
            yield return project.OutputReferencePath;
            foreach (var value in KeyStrings(project.Key).Concat(project.ParseOptions.SelectMany(
                pair => new[] { pair.Key, pair.Value })).Concat(project.CompilationOptions.SelectMany(
                pair => new[] { pair.Key, pair.Value })))
            {
                yield return value;
            }
            foreach (var document in project.Documents)
            {
                yield return document.Role;
                yield return document.Path;
                yield return document.Name;
                yield return document.Encoding.DecoderFallback;
                yield return document.Encoding.DecoderReplacement;
                yield return document.Encoding.EncoderFallback;
                yield return document.Encoding.EncoderReplacement;
                foreach (var value in KeyStrings(document.Project).Concat(document.Folders))
                {
                    yield return value;
                }
            }
            foreach (var reference in project.MetadataReferences)
            {
                yield return reference.Path;
                yield return reference.DocumentationPath;
                foreach (var alias in reference.Aliases)
                {
                    yield return alias;
                }
            }
            foreach (var analyzer in project.Analyzers)
            {
                yield return analyzer.OriginalPath;
                yield return analyzer.OriginDirectory;
                yield return analyzer.Sha256;
                foreach (var value in KeyStrings(analyzer.Consumer).Concat(analyzer.DependencyPaths))
                {
                    yield return value;
                }
            }
        }
        foreach (var edge in snapshot.ProjectReferences)
        {
            foreach (var value in KeyStrings(edge.From).Concat(KeyStrings(edge.To)).Concat(edge.Aliases))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string?> KeyStrings(InstanceKey key)
    {
        yield return key.ProjectPath;
        yield return key.InnerTargetFramework;
        foreach (var property in key.EffectiveProperties)
        {
            yield return property.Key;
            yield return property.Value;
        }
    }

    private static void ValidateKey(InstanceKey key)
    {
        Require(key is not null && key.EffectiveProperties is not null, "An instance key is null.");
        ValidatePath(key!.ProjectPath);
        ValidateString(key.InnerTargetFramework);
        Require(!string.IsNullOrWhiteSpace(key.InnerTargetFramework), "The inner target framework is missing.");
        foreach (var property in key.EffectiveProperties)
        {
            Require(EffectivePropertyKeys.Contains(property.Key), "An effective property is unsupported.");
            ValidateString(property.Value);
        }
        if (key.EffectiveProperties.TryGetValue("TargetFramework", out var framework))
        {
            Require(framework == key.InnerTargetFramework, "The inner target framework is inconsistent.");
        }
    }

    private static void ValidateProjectInputs(ProjectEntry project)
    {
        ValidateArray(project.Documents);
        ValidateArray(project.MetadataReferences);
        ValidateArray(project.Analyzers);
        var memberships = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in project.Documents)
        {
            Require(entry is not null && project.Key.Equals(entry.Project), "A document has the wrong owner.");
            ValidateKey(entry!.Project);
            Require(entry!.Role is "compile" or "additional" or "analyzer-config", "The document role is unsupported.");
            ValidatePath(entry.Path);
            ValidateString(entry.Name);
            ValidateStrings(entry.Folders);
            Require(Enum.IsDefined(entry.SourceKind), "The source kind is invalid.");
            ValidateEncoding(entry.Encoding);
            var membershipPath = OperatingSystem.IsWindows() ? entry.Path.ToUpperInvariant() : entry.Path;
            var membership = JsonSerializer.Serialize(new { entry.Role, Path = membershipPath, entry.Folders });
            Require(memberships.Add(membership), "A document membership is duplicated.");
        }
        var references = new HashSet<(string Path, MetadataImageKind Kind, string Aliases, bool Embed, string? Documentation)>();
        foreach (var entry in project.MetadataReferences)
        {
            Require(entry is not null, "A reference is null.");
            ValidatePath(entry!.Path);
            ValidatePath(entry.DocumentationPath, nullable: true);
            ValidateStrings(entry.Aliases);
            Require(Enum.IsDefined(entry.Kind), "The metadata kind is invalid.");
            var referencePath = OperatingSystem.IsWindows() ? entry.Path.ToUpperInvariant() : entry.Path;
            var documentationPath = OperatingSystem.IsWindows() ? entry.DocumentationPath?.ToUpperInvariant() : entry.DocumentationPath;
            Require(references.Add((referencePath, entry.Kind, JsonSerializer.Serialize(entry.Aliases),
                entry.EmbedInteropTypes, documentationPath)), "A metadata reference is duplicated.");
        }
        var analyzers = new HashSet<string>(PathComparer);
        foreach (var entry in project.Analyzers)
        {
            Require(entry is not null && project.Key.Equals(entry.Consumer), "An analyzer has the wrong consumer.");
            ValidateKey(entry!.Consumer);
            ValidatePath(entry!.OriginalPath);
            ValidatePath(entry.OriginDirectory);
            ValidateHash(entry.Sha256);
            ValidateArray(entry.DependencyPaths);
            Require(analyzers.Add(entry.OriginalPath), "An analyzer is duplicated.");
            var dependencies = new HashSet<string>(PathComparer);
            foreach (var path in entry.DependencyPaths)
            {
                ValidatePath(path);
                Require(dependencies.Add(path), "An analyzer dependency is duplicated.");
            }
        }
    }

    private static void ValidateEncoding(EncodingPolicy encoding)
    {
        Require(encoding is not null, "The encoding policy is null.");
        ValidateArray(encoding!.Preamble);
        Require(encoding.Preamble.Length <= 16, "The encoding preamble exceeds the bound.");
        if (encoding.CodePage is null)
        {
            Require(encoding.Preamble.IsEmpty && encoding.DecoderFallback == "unspecified"
                && encoding.EncoderFallback == "unspecified" && encoding.DecoderReplacement is null
                && encoding.EncoderReplacement is null, "Unspecified encoding carries fabricated policy.");
            return;
        }
        Require(encoding.CodePage > 0, "The encoding code page is invalid.");
        ValidateFallback(encoding.DecoderFallback, encoding.DecoderReplacement);
        ValidateFallback(encoding.EncoderFallback, encoding.EncoderReplacement);
    }

    private static void ValidateFallback(string kind, string? replacement)
    {
        Require(kind is "exception" or "replacement", "The encoding fallback is unsupported.");
        Require((kind == "replacement") == (replacement is not null), "The fallback replacement is inconsistent.");
        ValidateString(replacement, nullable: true);
    }

    private static void ValidateOptions(ImmutableSortedDictionary<string, string?> options, string[] keys, bool parse)
    {
        Require(options is not null && options.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(keys),
            "The option vector has missing or extra keys.");
        foreach (var pair in options!)
        {
            ValidateString(pair.Value, nullable: true);
            if (pair.Value is null)
            {
                Require(NullableOptionKeys.Contains(pair.Key), "A required option value is null.");
                continue;
            }
            using var document = JsonDocument.Parse(pair.Value, new JsonDocumentOptions { MaxDepth = 64 });
            var value = document.RootElement;
            ValidateJson(value);
            Require(JsonSerializer.Serialize(value) == pair.Value, "An option value is not canonical JSON.");
            ValidateOptionValue(pair.Key, value, parse);
        }
    }

    private static void ValidateOptionValue(string key, JsonElement value, bool parse)
    {
        switch (key)
        {
            case "SpecifiedLanguageVersion": case "LanguageVersion": RequireEnum<LanguageVersion>(value); break;
            case "SpecifiedKind": case "Kind": RequireEnum<SourceCodeKind>(value); break;
            case "DocumentationMode": RequireEnum<DocumentationMode>(value); break;
            case "OutputKind": RequireEnum<OutputKind>(value); break;
            case "OptimizationLevel": RequireEnum<OptimizationLevel>(value); break;
            case "Platform": RequireEnum<Platform>(value); break;
            case "GeneralDiagnosticOption": RequireEnum<ReportDiagnostic>(value); break;
            case "MetadataImportOptions": RequireEnum<MetadataImportOptions>(value); break;
            case "NullableContextOptions": RequireEnum<NullableContextOptions>(value); break;
            case "PreprocessorSymbolNames": case "Usings": RequireStringArray(value); break;
            case "Features": ValidateMap(value, diagnostic: false); break;
            case "SpecificDiagnosticOptions": ValidateMap(value, diagnostic: true); break;
            case "WarningLevel": Require(value.TryGetInt32(out var warningLevel) && warningLevel >= 0, "Invalid warning level."); break;
            case "CheckOverflow": case "AllowUnsafe": case "DelaySign": case "PublicSign":
            case "ConcurrentBuild": case "Deterministic": case "ReportSuppressedDiagnostics":
                Require(value.ValueKind is JsonValueKind.True or JsonValueKind.False, "A Boolean option is required."); break;
            case "CryptoPublicKey":
                Require(value.ValueKind == JsonValueKind.String, "A base64 public key is required.");
                Require(Convert.ToBase64String(Convert.FromBase64String(value.GetString()!)) == value.GetString(),
                    "The public key base64 is not canonical."); break;
            case "MetadataReferenceResolver": case "XmlReferenceResolver": case "SourceReferenceResolver":
            case "StrongNameProvider": case "AssemblyIdentityComparer": ValidateService(key, value); break;
            default: Require(!parse && value.ValueKind == JsonValueKind.String, "A string option is required."); break;
        }
    }

    private static void ValidateMap(JsonElement value, bool diagnostic)
    {
        Require(value.ValueKind == JsonValueKind.Object, "An option map is required.");
        string? previous = null;
        foreach (var pair in value.EnumerateObject())
        {
            Require(previous is null || StringComparer.Ordinal.Compare(previous, pair.Name) < 0, "An option map is not sorted.");
            if (diagnostic)
            {
                RequireEnum<ReportDiagnostic>(pair.Value);
            }
            else
            {
                Require(pair.Value.ValueKind == JsonValueKind.String, "A feature value must be a string.");
            }
            previous = pair.Name;
        }
    }

    private static void ValidateService(string key, JsonElement value)
    {
        Require(key != "MetadataReferenceResolver", "A non-null metadata resolver is unsupported.");
        Require(value.ValueKind == JsonValueKind.Object && value.TryGetProperty("Kind", out _), "A resolver descriptor is required.");
        var kind = value.GetProperty("Kind").GetString();
        switch (key, kind)
        {
            case ("AssemblyIdentityComparer", "default"):
            case ("AssemblyIdentityComparer", "DesktopAssemblyIdentityComparer"):
                RequireShape(value, "Kind"); break;
            case ("XmlReferenceResolver", "XmlFileResolver"):
                RequireShape(value, "Kind", "BaseDirectory");
                ValidatePath(value.GetProperty("BaseDirectory").GetString(), nullable: true); break;
            case ("SourceReferenceResolver", "SourceFileResolver"):
                RequireShape(value, "Kind", "SearchPaths", "BaseDirectory", "PathMap");
                RequireStringArray(value.GetProperty("SearchPaths"));
                ValidatePath(value.GetProperty("BaseDirectory").GetString(), nullable: true);
                Require(value.GetProperty("PathMap").ValueKind == JsonValueKind.Array, "A path map is required.");
                foreach (var pair in value.GetProperty("PathMap").EnumerateArray())
                {
                    RequireShape(pair, "Key", "Value");
                    Require(pair.GetProperty("Key").ValueKind == JsonValueKind.String
                        && pair.GetProperty("Value").ValueKind == JsonValueKind.String, "A path map entry is invalid.");
                }
                break;
            case ("StrongNameProvider", "DesktopStrongNameProvider"):
                RequireShape(value, "Kind", "KeyFileSearchPaths");
                RequireStringArray(value.GetProperty("KeyFileSearchPaths")); break;
            default: throw new InvalidDataException("The public option service is unsupported.");
        }
    }

    private static void RequireStringArray(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.Array, "A string array is required.");
        foreach (var item in value.EnumerateArray())
        {
            Require(item.ValueKind == JsonValueKind.String, "A string array member is invalid.");
        }
    }

    private static void RequireEnum<T>(JsonElement value) where T : struct, Enum =>
        Require(value.ValueKind == JsonValueKind.String && Enum.TryParse<T>(value.GetString(), out var parsed)
            && Enum.IsDefined(parsed) && parsed.ToString() == value.GetString(), "An enum option is invalid.");

    private static void ValidateHash(string hash) => Require(hash is { Length: 64 }
        && hash.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F'), "A SHA-256 hash is invalid.");

    private static void ValidatePath(string? path, bool nullable = false)
    {
        ValidateString(path, nullable);
        if (path is null)
        {
            return;
        }
        Require(Path.IsPathFullyQualified(path) && PathComparer.Equals(path, Path.GetFullPath(path))
            && !path.Contains('\0'), "A canonical absolute path is required.");
    }

    private static void ValidateString(string? value, bool nullable = false) =>
        Require(value is null ? nullable : value.Length <= MaximumString, "A required string is null or oversized.");

    private static void ValidateStrings(ImmutableArray<string> values)
    {
        ValidateArray(values);
        foreach (var value in values)
        {
            ValidateString(value);
        }
    }

    private static void ValidateArray<T>(ImmutableArray<T> values) =>
        Require(!values.IsDefault && values.Length <= MaximumMemberships, "An immutable array is default or oversized.");

    private static void Require([DoesNotReturnIf(false)] bool condition, string reason)
    {
        if (!condition)
        {
            throw new InvalidDataException(reason);
        }
    }

    private static bool IsMalformed(Exception exception) => exception is JsonException or ArgumentException
        or InvalidOperationException or NotSupportedException or FormatException or NullReferenceException;

    private const int MaximumBytes = 16 * 1024 * 1024;
    private const int MaximumString = 32768;
    private const int MaximumMemberships = 1000000;

    // Validate before each serializer flush, so an oversized value graph never allocates
    // its complete JSON representation before the byte budget is enforced.
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

        private void CheckBound(int count)
        {
            if (Position > MaximumBytes - count)
            {
                throw new InvalidDataException("The semantic snapshot exceeds the byte bound.");
            }
        }
    }

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 64,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private static readonly HashSet<string> EffectivePropertyKeys = new(StringComparer.Ordinal)
    {
        "Configuration", "Platform", "TargetFramework", "DesignTimeBuild", "SkipCompilerExecution", "ProvideCommandLineArgs",
        "BuildProjectReferences", "BuildingInsideVisualStudio"
    };
    private static readonly string[] ParseKeys =
    [
        "SpecifiedLanguageVersion", "LanguageVersion", "SpecifiedKind", "Kind", "DocumentationMode",
        "PreprocessorSymbolNames", "Features"
    ];
    private static readonly string[] CompilationKeys =
    [
        "OutputKind", "ModuleName", "MainTypeName", "ScriptClassName", "Usings", "OptimizationLevel", "CheckOverflow",
        "AllowUnsafe", "CryptoKeyContainer", "CryptoKeyFile", "CryptoPublicKey", "DelaySign", "PublicSign", "Platform",
        "GeneralDiagnosticOption", "WarningLevel", "SpecificDiagnosticOptions", "ConcurrentBuild", "Deterministic",
        "MetadataImportOptions", "NullableContextOptions", "ReportSuppressedDiagnostics", "MetadataReferenceResolver",
        "XmlReferenceResolver", "SourceReferenceResolver", "StrongNameProvider", "AssemblyIdentityComparer"
    ];
    private static readonly HashSet<string> NullableOptionKeys = new(StringComparer.Ordinal)
    {
        "ModuleName", "MainTypeName", "ScriptClassName", "CryptoKeyContainer", "CryptoKeyFile", "DelaySign",
        "MetadataReferenceResolver", "XmlReferenceResolver", "SourceReferenceResolver", "StrongNameProvider",
        "AssemblyIdentityComparer"
    };
}
