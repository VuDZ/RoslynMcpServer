using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Workspace.StateCache;
using RoslynMcpServer.Services.Workspace.StateCache.Models;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class WorkspaceSnapshotCodecTests
{
    public WorkspaceSnapshotCodecTests() => MsBuildBootstrapper.Register();

    [Fact]
    public void Portable_snapshot_round_trips()
    {
        var snapshot = PortableSnapshot();
        var encoded = WorkspaceSnapshotCodec.Encode(snapshot);
        var decoded = WorkspaceSnapshotCodec.Decode(encoded);
        Assert.Equal(encoded, WorkspaceSnapshotCodec.Encode(decoded));
        Assert.Equal(snapshot.SchemaVersion, decoded.SchemaVersion);
        Assert.Equal(snapshot.Profile, decoded.Profile);
        Assert.Equal(snapshot.Projects[0].Name, decoded.Projects[0].Name);
        Assert.Equal(snapshot.Projects[0].Documents[0].TextMetadata.Checksum, decoded.Projects[0].Documents[0].TextMetadata.Checksum);
        using var document = JsonDocument.Parse(encoded);
        var payload = document.RootElement.GetProperty("Payload").GetRawText();
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        Assert.Equal(checksum, document.RootElement.GetProperty("Checksum").GetString());
    }

    [Theory]
    [InlineData("envelope")]
    [InlineData("version")]
    [InlineData("profile")]
    public void Unknown_schema_is_rejected(string kind)
    {
        var encoded = WorkspaceSnapshotCodec.Encode(PortableSnapshot());
        using var document = JsonDocument.Parse(encoded);
        var payload = document.RootElement.GetProperty("Payload").GetRawText();
        byte[] mutated = kind switch
        {
            "envelope" => Envelope(payload, schema: "workspace-snapshot-envelope-v2"),
            "version" => Envelope(payload.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":2", StringComparison.Ordinal)),
            "profile" => Envelope(payload.Replace(OrdinaryWorkspaceCapture.Profile, "other-profile", StringComparison.Ordinal)),
            _ => throw new InvalidOperationException(kind),
        };
        var exception = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Decode(mutated));
        Assert.Contains("schema", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Truncated_payload_is_rejected()
    {
        var encoded = WorkspaceSnapshotCodec.Encode(PortableSnapshot());
        var exception = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Decode(encoded.AsSpan(0, encoded.Length - 8)));
        Assert.Contains("truncated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_json_key_is_rejected()
    {
        var encoded = Encoding.UTF8.GetString(WorkspaceSnapshotCodec.Encode(PortableSnapshot()));
        var duplicated = "{" + "\"Schema\":\"" + WorkspaceSnapshotCodec.EnvelopeSchema + "\"," + encoded[1..];
        var exception = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Decode(Encoding.UTF8.GetBytes(duplicated)));
        Assert.Contains("duplicate key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nested_duplicate_key_is_rejected_before_materializing_options()
    {
        var encoded = WorkspaceSnapshotCodec.Encode(PortableSnapshot());
        using var document = JsonDocument.Parse(encoded);
        var payload = document.RootElement.GetProperty("Payload").GetRawText();
        var needle = "\"Name\":\"InterceptorsPreview\"";
        var index = payload.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0);
        var mutated = payload.Insert(index, needle + ",");
        var exception = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Decode(Envelope(mutated)));
        Assert.Contains("duplicate key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extra_payload_key_is_rejected()
    {
        var encoded = WorkspaceSnapshotCodec.Encode(PortableSnapshot());
        using var document = JsonDocument.Parse(encoded);
        var payload = document.RootElement.GetProperty("Payload").GetRawText();
        var mutated = "{\"Extra\":true," + payload[1..];
        var exception = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Decode(Envelope(mutated)));
        Assert.Contains("missing or extra keys", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invalid_option_is_rejected_on_encode_and_decode()
    {
        var snapshot = PortableSnapshot();
        var project = snapshot.Projects[0];
        snapshot = snapshot with
        {
            Projects =
            [
                project with { ParseOptions = project.ParseOptions with { LanguageVersion = "NotAVersion" } },
            ],
        };
        var encode = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Encode(snapshot));
        Assert.Contains("enum option", encode.Message, StringComparison.OrdinalIgnoreCase);

        var valid = WorkspaceSnapshotCodec.Encode(PortableSnapshot());
        using var document = JsonDocument.Parse(valid);
        var payload = document.RootElement.GetProperty("Payload").GetRawText()
            .Replace("\"LanguageVersion\":\"CSharp10\"", "\"LanguageVersion\":\"NotAVersion\"", StringComparison.Ordinal);
        var decode = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Decode(Envelope(payload)));
        Assert.Contains("enum option", decode.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Document_checksum_mismatch_is_rejected()
    {
        var snapshot = PortableSnapshot();
        var project = snapshot.Projects[0];
        var document = project.Documents[0];
        var metadata = document.TextMetadata with { Checksum = [1, 2, 3] };
        snapshot = snapshot with
        {
            Projects = [project with { Documents = [document with { TextMetadata = metadata }] }],
        };
        var exception = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Encode(snapshot));
        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Envelope_checksum_failure_is_reported_before_option_validation()
    {
        var text = Encoding.UTF8.GetString(WorkspaceSnapshotCodec.Encode(PortableSnapshot()));
        var broken = text.Replace("\"DocumentationMode\":\"Parse\"", "\"DocumentationMode\":\"Nope\"", StringComparison.Ordinal);
        var exception = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Decode(Encoding.UTF8.GetBytes(broken)));
        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Payload_over_the_byte_bound_is_rejected_before_parse()
    {
        var payload = new byte[WorkspaceSnapshotCodec.MaximumBytes + 1];
        var exception = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Decode(payload));
        Assert.Contains("byte bound", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Individually_valid_strings_are_rejected_when_their_aggregate_exceeds_the_byte_bound()
    {
        var snapshot = PortableSnapshot();
        var project = snapshot.Projects[0];
        var document = project.Documents[0];
        var heavy = new string('x', WorkspaceSnapshotCodec.MaximumStringLength);
        var documents = Enumerable.Range(0, 520).Select(index => document with
        {
            Path = Canonical($"heavy{index}.cs"),
            Name = "F" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".cs",
            Folders = [heavy],
        }).ToArray();
        snapshot = snapshot with { Projects = [project with { Documents = documents }] };
        var exception = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Encode(snapshot));
        Assert.Contains("byte bound", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Individually_valid_arrays_are_rejected_when_their_aggregate_exceeds_the_membership_bound()
    {
        var encoded = WorkspaceSnapshotCodec.Encode(PortableSnapshot());
        using var document = JsonDocument.Parse(encoded);
        var payload = document.RootElement.GetProperty("Payload").GetRawText();
        var symbols = RepeatedJsonStrings(600_000);
        var usings = RepeatedJsonStrings(600_000);
        payload = ReplaceEmptyArray(payload, "PreprocessorSymbols", symbols);
        payload = ReplaceEmptyArray(payload, "Usings", usings);
        var exception = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Decode(Envelope(payload)));
        Assert.Contains("membership count", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_project_instance_is_rejected()
    {
        var snapshot = PortableSnapshot();
        snapshot = snapshot with { Projects = [snapshot.Projects[0], snapshot.Projects[0]] };
        var exception = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Encode(snapshot));
        Assert.Contains("Duplicate project", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Contradictory_language_version_is_rejected()
    {
        var snapshot = PortableSnapshot();
        var parse = snapshot.Projects[0].ParseOptions with { LanguageVersion = "CSharp1" };
        Reject(WithParse(snapshot, parse), "option value");
    }

    [Fact]
    public void Specified_default_round_trips_when_the_effective_version_matches()
    {
        var effective = new CSharpParseOptions(LanguageVersion.Default).LanguageVersion.ToString();
        var snapshot = PortableSnapshot();
        var parse = snapshot.Projects[0].ParseOptions with
        {
            SpecifiedLanguageVersion = "Default",
            LanguageVersion = effective,
        };
        RoundTrip(WithParse(snapshot, parse));
    }

    [Fact]
    public void Specified_default_with_a_different_effective_version_is_rejected()
    {
        var effective = new CSharpParseOptions(LanguageVersion.Default).LanguageVersion.ToString();
        var wrong = effective == "CSharp1" ? "CSharp2" : "CSharp1";
        var snapshot = PortableSnapshot();
        var parse = snapshot.Projects[0].ParseOptions with
        {
            SpecifiedLanguageVersion = "Default",
            LanguageVersion = wrong,
        };
        Reject(WithParse(snapshot, parse), "option value");
    }

    [Fact]
    public void Invalid_preprocessor_symbol_is_rejected()
    {
        var snapshot = PortableSnapshot();
        var parse = snapshot.Projects[0].ParseOptions with { PreprocessorSymbols = ["NOT A SYMBOL"] };
        Reject(WithParse(snapshot, parse), "option value");
    }

    [Fact]
    public void Unproven_metadata_producer_is_rejected()
    {
        var snapshot = PortableSnapshot();
        var key = snapshot.Projects[0].Key;
        var paths = snapshot.InputPaths.ToArray();
        var index = Array.FindIndex(paths, path => path.Occurrences[0].Role == "MetadataDependency");
        var metadata = paths[index];
        paths[index] = metadata with
        {
            Occurrences =
            [
                metadata.Occurrences[0] with
                {
                    Producer = key,
                    ProducerSource = SnapshotOccurrenceBinder.MetadataProducerSource,
                },
            ],
        };
        Reject(snapshot with { InputPaths = paths }, "metadata producer");
    }

    [Fact]
    public void Empty_input_paths_are_rejected() =>
        Reject(PortableSnapshot() with { InputPaths = [] }, "no input occurrence");

    [Fact]
    public void Generated_source_with_a_user_input_role_is_rejected()
    {
        var snapshot = PortableSnapshot();
        var project = snapshot.Projects[0];
        snapshot = snapshot with
        {
            Projects = [project with { Documents = [project.Documents[0] with { IsGenerated = true }] }],
        };
        Reject(snapshot, "does not match");
    }

    [Fact]
    public void Linked_owners_and_two_document_kinds_round_trip() => RoundTrip(LinkedSnapshot());

    [Fact]
    public void Proven_metadata_producer_round_trips() => RoundTrip(ProvenProducerSnapshot());

    [Fact]
    public async Task Captured_control_round_trips()
    {
        MsBuildBootstrapper.Register();
        var root = FindRoot();
        var path = Path.Combine(root, "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var host = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(host).LoadProjectInfoAsync(path, cancellationToken: timeout.Token);
        host.AddProjects(descriptors);
        var properties = new SnapshotLoadProperties(new(null, null, null), new(null, null, null));
        var result = await OrdinaryWorkspaceCapture.CaptureAsync(
            host.CurrentSolution,
            descriptors,
            path,
            properties,
            originalAnalyzerPathsProven: true,
            timeout.Token);
        Assert.True(result.UnsupportedReason is null, result.UnsupportedReason);
        var snapshot = Assert.IsType<WorkspaceSnapshot>(result.Snapshot);
        var encoded = WorkspaceSnapshotCodec.Encode(snapshot);
        var decoded = WorkspaceSnapshotCodec.Decode(encoded);
        Assert.Equal(encoded, WorkspaceSnapshotCodec.Encode(decoded));
        Assert.Equal(snapshot.Projects.Length, decoded.Projects.Length);
        Assert.Equal(snapshot.InputPaths.Length, decoded.InputPaths.Length);
    }

    private static byte[] Envelope(string payloadJson, string? schema = null, string? checksum = null)
    {
        checksum ??= Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));
        schema ??= WorkspaceSnapshotCodec.EnvelopeSchema;
        return Encoding.UTF8.GetBytes(
            "{\"Schema\":\"" + schema + "\",\"ChecksumAlgorithm\":\"" + WorkspaceSnapshotCodec.ChecksumAlgorithmName
            + "\",\"Checksum\":\"" + checksum + "\",\"Payload\":" + payloadJson + "}");
    }

    private static string ReplaceEmptyArray(string payload, string property, string items)
    {
        var needle = "\"" + property + "\":[]";
        var index = payload.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0, property);
        return payload.Remove(index, needle.Length).Insert(index, "\"" + property + "\":[" + items + "]");
    }

    private static string RepeatedJsonStrings(int count)
    {
        var builder = new StringBuilder(count * 4);
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append("\"a\"");
        }

        return builder.ToString();
    }

    private static WorkspaceSnapshot PortableSnapshot()
    {
        var root = Canonical(string.Empty);
        var key = new SnapshotProjectKey(Canonical("App.csproj"), "Debug", "AnyCPU", "net10.0");
        var documentPath = Canonical("Program.cs");
        var metadataPath = Canonical(Path.Combine("lib", "System.Runtime.dll"));
        var analyzerPath = Canonical(Path.Combine("analyzers", "Some.dll"));
        var outputPath = Canonical(Path.Combine("bin", "App.dll"));
        var checksum = new byte[20];
        checksum[0] = 1;
        var document = new SnapshotDocument(
            "source",
            documentPath,
            "Program.cs",
            [],
            "Regular",
            false,
            new SnapshotTextMetadata(65001, [], "Sha1", checksum));
        var project = new SnapshotProject(
            key,
            new SnapshotEvaluatedProperties(
                new SnapshotPropertySet(key.Configuration, key.Platform, key.TargetFramework),
                OrdinaryWorkspaceCapture.EvaluatedPropertySource,
                []),
            "App",
            "App",
            LanguageNames.CSharp,
            outputPath,
            null,
            "App",
            outputPath,
            null,
            new SnapshotParseOptions(
                "CSharp10",
                "CSharp10",
                "Parse",
                "Regular",
                [],
                [new SnapshotNameValue("InterceptorsPreview", "true")]),
            CompilationOptions(root),
            [document],
            [],
            [
                new SnapshotMetadataReference(
                    metadataPath,
                    "Assembly",
                    [],
                    false,
                    OrdinaryWorkspaceCapture.BinaryDocumentationPolicy),
            ],
            [
                new SnapshotAnalyzerReference(analyzerPath, OrdinaryWorkspaceCapture.AnalyzerIdentitySource),
            ]);
        return new WorkspaceSnapshot(
            OrdinaryWorkspaceCapture.SchemaVersion,
            OrdinaryWorkspaceCapture.Profile,
            "5.9.0.0",
            root,
            new SnapshotLoadProperties(new(null, null, null), new(null, null, null)),
            [project],
            [
                Occurrence(documentPath, key, "source", "UserInput", SnapshotOccurrenceBinder.DocumentRoleSource, null, null),
                Occurrence(Canonical("App.csproj"), key, null, "EvaluationInput", SnapshotOccurrenceBinder.ProjectFileRoleSource, null, null),
                Occurrence(metadataPath, key, null, "MetadataDependency", SnapshotOccurrenceBinder.MetadataRoleSource, null, null),
                Occurrence(analyzerPath, key, null, "AnalyzerDependency", OrdinaryWorkspaceCapture.AnalyzerIdentitySource, null, null),
                Occurrence(outputPath, key, null, "Output", SnapshotOccurrenceBinder.OutputRoleSource, key, SnapshotOccurrenceBinder.OutputRoleSource),
            ]);
    }

    private static WorkspaceSnapshot LinkedSnapshot()
    {
        var snapshot = PortableSnapshot();
        var app = snapshot.Projects[0];
        var source = app.Documents[0];
        var libKey = new SnapshotProjectKey(Canonical("Lib.csproj"), "Debug", "AnyCPU", "net10.0");
        var libOutput = Canonical(Path.Combine("bin", "Lib.dll"));
        var lib = ProjectLike(app, libKey, "Lib", libOutput, [source, source with { Kind = "additional" }]);
        var shared = snapshot.InputPaths[0];
        var paths = snapshot.InputPaths.ToArray();
        paths[0] = shared with
        {
            Occurrences =
            [
                shared.Occurrences[0],
                new SnapshotInputOccurrence(
                    libKey, "source", "UserInput", SnapshotOccurrenceBinder.DocumentRoleSource, null, null, true),
                new SnapshotInputOccurrence(
                    libKey, "additional", "AdditionalFile", SnapshotOccurrenceBinder.DocumentRoleSource, null, null, true),
            ],
        };
        return snapshot with
        {
            Projects = [app, lib],
            InputPaths =
            [
                ..paths,
                Occurrence(libKey.ProjectPath, libKey, null, "EvaluationInput", SnapshotOccurrenceBinder.ProjectFileRoleSource, null, null),
                Occurrence(libOutput, libKey, null, "Output", SnapshotOccurrenceBinder.OutputRoleSource, libKey, SnapshotOccurrenceBinder.OutputRoleSource),
            ],
        };
    }

    private static WorkspaceSnapshot ProvenProducerSnapshot()
    {
        var snapshot = PortableSnapshot();
        var app = snapshot.Projects[0];
        var metadataPath = app.MetadataReferences[0].Path;
        var libKey = new SnapshotProjectKey(Canonical("Lib.csproj"), "Debug", "AnyCPU", "net10.0");
        var lib = ProjectLike(app, libKey, "Lib", metadataPath, []);
        var paths = snapshot.InputPaths.ToArray();
        var index = Array.FindIndex(paths, path => path.Occurrences[0].Role == "MetadataDependency");
        var metadata = paths[index];
        paths[index] = metadata with
        {
            Occurrences =
            [
                metadata.Occurrences[0] with
                {
                    Producer = libKey,
                    ProducerSource = SnapshotOccurrenceBinder.MetadataProducerSource,
                },
                new SnapshotInputOccurrence(
                    libKey,
                    null,
                    "Output",
                    SnapshotOccurrenceBinder.OutputRoleSource,
                    libKey,
                    SnapshotOccurrenceBinder.OutputRoleSource,
                    true),
            ],
        };
        return snapshot with
        {
            Projects =
            [
                app with { ProjectReferences = [new SnapshotProjectReference(libKey, [], false)] },
                lib,
            ],
            InputPaths =
            [
                ..paths,
                Occurrence(libKey.ProjectPath, libKey, null, "EvaluationInput", SnapshotOccurrenceBinder.ProjectFileRoleSource, null, null),
            ],
        };
    }

    private static SnapshotProject ProjectLike(
        SnapshotProject template,
        SnapshotProjectKey key,
        string name,
        string outputPath,
        SnapshotDocument[] documents) =>
        new(
            key,
            new SnapshotEvaluatedProperties(
                new SnapshotPropertySet(key.Configuration, key.Platform, key.TargetFramework),
                OrdinaryWorkspaceCapture.EvaluatedPropertySource,
                []),
            name,
            name,
            LanguageNames.CSharp,
            outputPath,
            null,
            name,
            outputPath,
            null,
            template.ParseOptions,
            template.CompilationOptions,
            documents,
            [],
            [],
            []);

    private static WorkspaceSnapshot WithParse(WorkspaceSnapshot snapshot, SnapshotParseOptions parse)
    {
        var project = snapshot.Projects[0];
        return snapshot with { Projects = [project with { ParseOptions = parse }] };
    }

    private static void RoundTrip(WorkspaceSnapshot snapshot)
    {
        var encoded = WorkspaceSnapshotCodec.Encode(snapshot);
        var decoded = WorkspaceSnapshotCodec.Decode(encoded);
        Assert.Equal(encoded, WorkspaceSnapshotCodec.Encode(decoded));
    }

    private static void Reject(WorkspaceSnapshot snapshot, string fragment)
    {
        var encode = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Encode(snapshot));
        Assert.Contains(fragment, encode.Message, StringComparison.OrdinalIgnoreCase);
        var payload = JsonSerializer.Serialize(snapshot);
        var decode = Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Decode(Envelope(payload)));
        Assert.Contains(fragment, decode.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SnapshotInputPath Occurrence(
        string path,
        SnapshotProjectKey owner,
        string? documentKind,
        string role,
        string roleSource,
        SnapshotProjectKey? producer,
        string? producerSource) =>
        new(
            path,
            [
                new SnapshotInputOccurrence(owner, documentKind, role, roleSource, producer, producerSource, true),
            ]);

    private static SnapshotCompilationOptions CompilationOptions(string root) =>
        new(
            "csharp-command-line-debug-plus-v1",
            "DynamicallyLinkedLibrary",
            null,
            null,
            null,
            [],
            "Debug",
            false,
            false,
            null,
            null,
            [],
            null,
            false,
            "AnyCpu",
            "Default",
            4,
            [],
            true,
            true,
            "Public",
            "Disable",
            false,
            "desktop-default",
            NoneResolver(),
            NoneResolver(),
            new SnapshotResolver("command-line-project-factory-v1", root, [], []),
            SnapshotSigningProfile.IndependentPolicy,
            "none");

    private static SnapshotResolver NoneResolver() => new("none", null, [], []);

    private static string Canonical(string relative)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "snapshot-codec"));
        return relative.Length == 0 ? root : Path.GetFullPath(Path.Combine(root, relative));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RoslynMcpServer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
