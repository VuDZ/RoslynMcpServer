using System.Security.Cryptography;
using System.Text;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

public enum OutputPathMode
{
    RedirectedMissingAnalyzerPath,
    SdkDefaultCorrectPath,
}

internal sealed class GeneratorConsumerFixture : IDisposable
{
    public const string MarkerV1 = "V1";
    public const string MarkerV2 = "V2";
    public const string MarkerA = "A";
    public const string MarkerB = "B";
    public const string MarkerForeign = "FOREIGN";

    private GeneratorConsumerFixture(string root)
    {
        Root = root;
    }

    public string Root { get; }
    public string SolutionPath { get; private set; } = "";
    public string GeneratorProjectPath { get; private set; } = "";
    public string GeneratorSourcePath { get; private set; } = "";
    public string ConsumerProjectPath { get; private set; } = "";
    public string ConsumerSourcePath { get; private set; } = "";
    public string? HelperProjectPath { get; private set; }
    public string? HelperSourcePath { get; private set; }
    public IReadOnlyList<string> ExtraConsumerSourcePaths { get; private set; } = Array.Empty<string>();
    public string? ForeignDllPath { get; private set; }
    public string? MissingForeignPath { get; private set; }
    public IReadOnlyDictionary<string, byte[]> ProjectFileBytes { get; private set; } =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

    public static GeneratorConsumerFixture Create(
        OutputPathMode outputPathMode,
        string marker = MarkerV1,
        int extraConsumers = 0,
        bool foreignAnalyzer = false,
        bool missingForeignPath = false,
        string assemblyName = "Generator",
        bool privateHelper = false,
        string helperVersion = "1.0.0.0")
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "RoslynMcpServer.Epoch1",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fixture = new GeneratorConsumerFixture(root);
        fixture.WriteLayout(
            outputPathMode,
            marker,
            extraConsumers,
            foreignAnalyzer,
            missingForeignPath,
            assemblyName,
            privateHelper,
            helperVersion);
        return fixture;
    }

    public void SetGeneratorMarker(string marker)
    {
        File.WriteAllText(GeneratorSourcePath, CreateGeneratorSource(marker));
    }

    public string ReadConsumerSource() => File.ReadAllText(ConsumerSourcePath);

    public void WriteConsumerSource(string text) => File.WriteAllText(ConsumerSourcePath, text);

    public string WithConsumerComment(string comment)
    {
        var current = ReadConsumerSource();
        var updated = System.Text.RegularExpressions.Regex.Replace(
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

    public string? FindGeneratorOutputDll()
    {
        var artifacts = Path.Combine(Root, "artifacts", "Generator");
        if (Directory.Exists(artifacts))
        {
            var hit = Directory.EnumerateFiles(artifacts, "Generator.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (hit is not null)
            {
                return hit;
            }
        }

        var bin = Path.Combine(Root, "Generator", "bin");
        if (Directory.Exists(bin))
        {
            return Directory.EnumerateFiles(bin, "Generator.dll", SearchOption.AllDirectories).FirstOrDefault();
        }

        return null;
    }

    public string? FindHelperOutputDll()
    {
        var artifacts = Path.Combine(Root, "artifacts", "Generator.Helpers");
        if (Directory.Exists(artifacts))
        {
            var hit = Directory.EnumerateFiles(artifacts, "Generator.Helpers.dll", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (hit is not null)
            {
                return hit;
            }
        }

        var bin = Path.Combine(Root, "Generator.Helpers", "bin");
        if (Directory.Exists(bin))
        {
            return Directory.EnumerateFiles(bin, "Generator.Helpers.dll", SearchOption.AllDirectories).FirstOrDefault();
        }

        return null;
    }

    public IReadOnlyDictionary<string, string> HashProjectFiles()
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories))
        {
            hashes[file] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return hashes;
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

    private void WriteLayout(
        OutputPathMode outputPathMode,
        string marker,
        int extraConsumers,
        bool foreignAnalyzer,
        bool missingForeignPath,
        string assemblyName,
        bool privateHelper,
        string helperVersion)
    {
        if (outputPathMode == OutputPathMode.RedirectedMissingAnalyzerPath)
        {
            File.WriteAllText(
                Path.Combine(Root, "Directory.Build.props"),
                """
                <Project>
                  <PropertyGroup>
                    <BaseOutputPath>$(MSBuildThisFileDirectory)artifacts\$(MSBuildProjectName)\</BaseOutputPath>
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
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
                <IsRoslynComponent>true</IsRoslynComponent>
                <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
                <AssemblyName>{assemblyName}</AssemblyName>
                <Version>0.0.0.0</Version>
                <Deterministic>true</Deterministic>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
              </ItemGroup>
              {(privateHelper ? """
              <ItemGroup>
                <ProjectReference Include="..\Generator.Helpers\Generator.Helpers.csproj" PrivateAssets="all" />
              </ItemGroup>
              """ : "")}
            </Project>
            """);
        if (privateHelper)
        {
            WriteHelperProject(helperVersion);
        }

        GeneratorSourcePath = Path.Combine(generatorDir, "MarkerGenerator.cs");
        File.WriteAllText(GeneratorSourcePath, CreateGeneratorSource(marker, privateHelper));

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
            var analyzerItem = "";
            if (name == "Consumer" && foreignAnalyzer)
            {
                ForeignDllPath = Path.Combine(Root, "external", "Generator.dll");
                analyzerItem = """
                    <ItemGroup>
                      <Analyzer Include="..\external\Generator.dll" />
                    </ItemGroup>
                    """;
            }
            else if (name == "Consumer" && missingForeignPath)
            {
                MissingForeignPath = Path.Combine(Root, "missing", "Generator.dll");
                analyzerItem = """
                    <ItemGroup>
                      <Analyzer Include="..\missing\Generator.dll" />
                    </ItemGroup>
                    """;
            }

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
                  {analyzerItem}
                </Project>
                """);
            var source = Path.Combine(dir, "MarkerConsumer.cs");
            File.WriteAllText(source, CreateConsumerSource(name, privateHelper));
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
        File.WriteAllText(SolutionPath, CreateSolution(consumerNames, privateHelper), Encoding.UTF8);

        if (foreignAnalyzer)
        {
            WriteForeignGeneratorProject(MarkerForeign, assemblyName);
        }

        var bytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories))
        {
            bytes[file] = File.ReadAllBytes(file);
        }

        ProjectFileBytes = bytes;
    }

    private void WriteForeignGeneratorProject(string marker, string assemblyName)
    {
        var dir = Path.Combine(Root, "ForeignGenerator");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "ForeignGenerator.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
                <IsRoslynComponent>true</IsRoslynComponent>
                <AssemblyName>{assemblyName}</AssemblyName>
                <Version>0.0.0.0</Version>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                <OutputPath>$(MSBuildThisFileDirectory)..\external\</OutputPath>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "MarkerGenerator.cs"), CreateGeneratorSource(marker));
        Directory.CreateDirectory(Path.Combine(Root, "external"));
        ForeignDllPath = Path.Combine(Root, "external", assemblyName + ".dll");
    }

    private void WriteHelperProject(string helperVersion)
    {
        var dir = Path.Combine(Root, "Generator.Helpers");
        Directory.CreateDirectory(dir);
        HelperProjectPath = Path.Combine(dir, "Generator.Helpers.csproj");
        File.WriteAllText(
            HelperProjectPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
                <AssemblyName>Generator.Helpers</AssemblyName>
                <Version>{helperVersion}</Version>
                <Deterministic>true</Deterministic>
              </PropertyGroup>
            </Project>
            """);
        HelperSourcePath = Path.Combine(dir, "HelperInfo.cs");
        File.WriteAllText(HelperSourcePath, CreateHelperSource(helperVersion));
    }

    public void SetHelperVersionComment(string comment)
    {
        if (HelperSourcePath is null)
        {
            throw new InvalidOperationException("Fixture has no helper project.");
        }

        File.WriteAllText(HelperSourcePath, CreateHelperSource(comment));
    }

    internal static string CreateHelperSource(string versionOrComment) =>
        $$"""
        namespace Generator.Helpers;

        public static class HelperInfo
        {
            public static string Name { get; } = "{{versionOrComment}}";
        }
        """;

    internal static string CreateGeneratorSource(string marker, bool privateHelper = false)
    {
        var helperUse = privateHelper
            ? "                    _ = Generator.Helpers.HelperInfo.Name;"
            : "";
        var source = """
            using Microsoft.CodeAnalysis;

            [Generator]
            public sealed class MarkerGenerator : IIncrementalGenerator
            {
                public void Initialize(IncrementalGeneratorInitializationContext context)
                {
            __HELPER__
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
        return source
            .Replace("__HELPER__", helperUse, StringComparison.Ordinal)
            .Replace("__MARKER__", marker, StringComparison.Ordinal);
    }

    internal static string CreateConsumerSource(string classPrefix, bool privateHelper = false) =>
        privateHelper
            ? $$"""
            internal static class {{classPrefix}}MarkerConsumer
            {
                // edit-target
                public static string GetMarker() => "no-generator";
            }
            """
            : $$"""
            internal static class {{classPrefix}}MarkerConsumer
            {
                // edit-target
                public static string GetMarker() => GeneratedMarker.Version;
            }
            """;

    private static string CreateSolution(IReadOnlyList<string> consumerNames, bool includeHelper)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Visual Studio Version 17");
        var helperId = "{10101010-1010-1010-1010-101010101010}";
        if (includeHelper)
        {
            sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"Generator.Helpers\", \"Generator.Helpers\\Generator.Helpers.csproj\", \"{helperId}\"");
            sb.AppendLine("EndProject");
        }

        var generatorId = "{11111111-1111-1111-1111-111111111111}";
        sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"Generator\", \"Generator\\Generator.csproj\", \"{generatorId}\"");
        sb.AppendLine("EndProject");
        var ids = new List<string>();
        if (includeHelper)
        {
            ids.Add(helperId);
        }

        ids.Add(generatorId);
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
