using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

public enum OutputPathMode
{
    /// <summary>
    /// A repo-level <c>Directory.Build.props</c> redirects the output and turns off the TFM suffix,
    /// so the analyzer path a consumer restores does not exist until the overlay republishes it.
    /// </summary>
    RedirectedMissingAnalyzerPath,

    /// <summary>
    /// SDK defaults: the analyzer path in the consumer exists and is the real build output, which
    /// the overlay must shadow rather than replace.
    /// </summary>
    SdkDefaultCorrectPath,
}

/// <summary>
/// Generator plus consumer solution used by the lifecycle host tests. The default mode is the SDK
/// layout; the redirected layout is opt-in for the V1-to-V2 identity cases.
/// </summary>
internal sealed class GeneratorConsumerFixture : IDisposable
{
    public const string MarkerV1 = "V1";
    public const string MarkerV2 = "V2";

    private GeneratorConsumerFixture(string root, OutputPathMode outputPathMode)
    {
        Root = root;
        OutputPathMode = outputPathMode;
    }

    public string Root { get; }
    public OutputPathMode OutputPathMode { get; }
    public string SolutionPath { get; private set; } = "";
    public string GeneratorProjectPath { get; private set; } = "";
    public string GeneratorSourcePath { get; private set; } = "";
    public string ConsumerProjectPath { get; private set; } = "";
    public string ConsumerSourcePath { get; private set; } = "";
    public IReadOnlyList<string> ExtraConsumerSourcePaths { get; private set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, byte[]> ProjectFileBytes { get; private set; } =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

    public static GeneratorConsumerFixture Create(
        OutputPathMode outputPathMode = OutputPathMode.SdkDefaultCorrectPath,
        string marker = MarkerV1,
        int extraConsumers = 0)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "RoslynMcpServer.Epoch1",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fixture = new GeneratorConsumerFixture(root, outputPathMode);
        fixture.WriteLayout(outputPathMode, marker, extraConsumers);
        return fixture;
    }

    public void SetGeneratorMarker(string marker)
    {
        File.WriteAllText(GeneratorSourcePath, CreateGeneratorSource(marker));
    }

    /// <summary>
    /// Forces the next generator build to produce different bytes. The generator project sets
    /// <c>Deterministic=true</c>, so the same source always compiles to the same DLL, and neither a
    /// comment nor a plain rebuild proves anything. The unique token therefore goes into the marker of
    /// the generated source, which the generator embeds as a constant; its literal uses doubled quotes
    /// because the generated code lives in a verbatim string.
    /// </summary>
    /// <returns>The token the rebuilt DLL embeds.</returns>
    public string ForceGeneratorOutputChange()
    {
        var source = File.ReadAllText(GeneratorSourcePath);
        const string MarkerPrefix = "public const string Version = \"\"";
        var markerStart = source.IndexOf(MarkerPrefix, StringComparison.Ordinal);
        if (markerStart < 0)
        {
            throw new InvalidOperationException("generator source has no marker constant: " + GeneratorSourcePath);
        }

        // The literal closes with doubled quotes; the token joins the marker value so the generator
        // embeds it, which the compiler cannot fold away.
        var literalEnd = source.IndexOf("\"\";", markerStart, StringComparison.Ordinal);
        if (literalEnd < 0)
        {
            throw new InvalidOperationException("generator marker constant is not terminated: " + GeneratorSourcePath);
        }

        var token = "rebuild-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(
            GeneratorSourcePath,
            source.Insert(literalEnd, "." + token));
        return token;
    }

    public void MakeGeneratorMultiTargeted()
    {
        var text = File.ReadAllText(GeneratorProjectPath)
            .Replace(
                "<TargetFramework>netstandard2.0</TargetFramework>",
                "<TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>",
                StringComparison.Ordinal);
        File.WriteAllText(GeneratorProjectPath, text);
    }

    public static void SetProjectTargetFramework(string projectPath, string targetFramework)
    {
        var text = File.ReadAllText(projectPath)
            .Replace(
                "<TargetFramework>netstandard2.0</TargetFramework>",
                $"<TargetFramework>{targetFramework}</TargetFramework>",
                StringComparison.Ordinal);
        File.WriteAllText(projectPath, text);
    }

    public string ReadConsumerSource() => File.ReadAllText(ConsumerSourcePath);

    public string WithConsumerComment(string comment)
    {
        var current = ReadConsumerSource();
        var updated = Regex.Replace(
            current,
            @"// edit-target.*",
            "// edit-target " + comment);
        if (updated == current)
        {
            updated = current.Replace(
                "// edit-target",
                "// edit-target " + comment,
                StringComparison.Ordinal);
        }

        return updated;
    }

    /// <summary>
    /// Locates the generator's own build output. The redirected layout switches the TFM suffix off, so
    /// depending on which <c>Directory.Build.props</c> the evaluation saw, the build lands in
    /// <c>artifacts</c> or in <c>Generator\bin</c>; the SDK layout keeps the <c>bin\&lt;Configuration&gt;\&lt;tfm&gt;</c>
    /// shape. The freshest real output wins, and reference assemblies under <c>obj\...\ref</c> are never
    /// returned: they are rewritten on every compile even when the IL did not change.
    /// </summary>
    public string? FindGeneratorOutputDll()
    {
        return Candidates().OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

        IEnumerable<string> Candidates()
        {
            if (OutputPathMode == OutputPathMode.RedirectedMissingAnalyzerPath)
            {
                var redirected = new[]
                {
                    Path.Combine(Root, "artifacts", "Generator.dll"),
                    Path.Combine(Root, "artifacts", "GeneratorDebug", "Generator.dll"),
                };
                foreach (var candidate in redirected.Where(File.Exists))
                {
                    yield return candidate;
                }
            }

            foreach (var root in new[] { Path.Combine(Root, "artifacts"), Path.Combine(Root, "Generator", "bin") })
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var candidate in Directory.EnumerateFiles(root, "Generator.dll", SearchOption.AllDirectories)
                             .Where(path => !IsReferenceAssembly(path)))
                {
                    yield return candidate;
                }
            }
        }

        static bool IsReferenceAssembly(string path) =>
            path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}refint{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ProjectBytesUnchanged(
        IReadOnlyDictionary<string, byte[]> original,
        IReadOnlyDictionary<string, string> currentHashes)
    {
        if (original.Count != currentHashes.Count)
        {
            return false;
        }

        foreach (var pair in original)
        {
            if (!currentHashes.TryGetValue(pair.Key, out var hash))
            {
                return false;
            }

            if (!string.Equals(hash, Convert.ToHexString(SHA256.HashData(pair.Value)), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Best-effort: generators and MSBuild may keep short-lived file locks.
        }
    }

    internal static string CreateGeneratorSource(string marker)
    {
        var source = """
            using Microsoft.CodeAnalysis;

            [Generator]
            public sealed class MarkerGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
                    context.RegisterPostInitializationOutput(ctx =>
                    {
                        ctx.AddSource("GeneratedMarker.g.cs", @"
            internal static class GeneratedMarker
            {
                public const string Version = ""__MARKER__"";
            }
            ");
                    });
                }
            }
            """;
        return source.Replace("__MARKER__", marker, StringComparison.Ordinal);
    }

    internal static string CreateConsumerSource(string classPrefix) =>
        $$"""
        internal static class {{classPrefix}}MarkerConsumer
        {
            // edit-target
            public static string GetMarker() => GeneratedMarker.Version;
        }
        """;

    private void WriteLayout(OutputPathMode outputPathMode, string marker, int extraConsumers)
    {
        if (outputPathMode == OutputPathMode.RedirectedMissingAnalyzerPath)
        {
            File.WriteAllText(
                Path.Combine(Root, "Directory.Build.props"),
                """
                <Project>
                  <PropertyGroup>
                    <BaseOutputPath>$(MSBuildThisFileDirectory)artifacts\$(MSBuildProjectName)</BaseOutputPath>
                    <OutputPath>$(BaseOutputPath)$(Configuration)\</OutputPath>
                    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                  </PropertyGroup>
                </Project>
                """);
        }

        var generatorDir = Path.Combine(Root, "Generator");
        Directory.CreateDirectory(generatorDir);
        GeneratorProjectPath = Path.Combine(generatorDir, "Generator.csproj");
        File.WriteAllText(
            GeneratorProjectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
                <IsRoslynComponent>true</IsRoslynComponent>
                <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
                <AssemblyName>Generator</AssemblyName>
                <Version>0.0.0.0</Version>
                <Deterministic>true</Deterministic>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);
        GeneratorSourcePath = Path.Combine(generatorDir, "MarkerGenerator.cs");
        File.WriteAllText(GeneratorSourcePath, CreateGeneratorSource(marker));

        var consumerNames = new List<string> { "Consumer" };
        for (var i = 1; i <= extraConsumers; i++)
        {
            consumerNames.Add("Consumer" + i);
        }

        var extraSources = new List<string>();
        foreach (var name in consumerNames)
        {
            var dir = Path.Combine(Root, name);
            Directory.CreateDirectory(dir);
            var csproj = Path.Combine(dir, name + ".csproj");
            File.WriteAllText(
                csproj,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>netstandard2.0</TargetFramework>
                    <LangVersion>latest</LangVersion>
                    <Nullable>enable</Nullable>
                    <AssemblyName>{name}</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\Generator\Generator.csproj"
                                      OutputItemType="Analyzer"
                                      ReferenceOutputAssembly="false" />
                  </ItemGroup>
                </Project>
                """);
            var source = Path.Combine(dir, "MarkerConsumer.cs");
            File.WriteAllText(source, CreateConsumerSource(name));
            if (name == "Consumer")
            {
                ConsumerProjectPath = csproj;
                ConsumerSourcePath = source;
            }
            else
            {
                extraSources.Add(source);
            }
        }

        ExtraConsumerSourcePaths = extraSources;

        SolutionPath = Path.Combine(Root, "Repro.sln");
        File.WriteAllText(SolutionPath, CreateSolution(consumerNames), Encoding.UTF8);

        var bytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories))
        {
            bytes[file] = File.ReadAllBytes(file);
        }

        ProjectFileBytes = bytes;
    }

    private static string CreateSolution(IReadOnlyList<string> consumerNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Visual Studio Version 17");
        var generatorId = "{11111111-1111-1111-1111-111111111111}";
        sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"Generator\", \"Generator\\Generator.csproj\", \"{generatorId}\"");
        sb.AppendLine("EndProject");

        var ids = new List<string> { generatorId };
        for (var i = 0; i < consumerNames.Count; i++)
        {
            var id = "{22222222-2222-2222-2222-" + (i + 1).ToString("000000000000") + "}";
            ids.Add(id);
            sb.AppendLine(
                $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{consumerNames[i]}\", \"{consumerNames[i]}\\{consumerNames[i]}.csproj\", \"{id}\"");
            sb.AppendLine("EndProject");
        }

        sb.AppendLine("Global");
        sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        sb.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var id in ids)
        {
            sb.AppendLine($"\t\t{id}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            sb.AppendLine($"\t\t{id}.Debug|Any CPU.Build.0 = Debug|Any CPU");
        }

        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("EndGlobal");
        return sb.ToString();
    }
}
