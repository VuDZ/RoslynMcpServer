using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class ProjectRenameHelperTests
{
    [Fact]
    public void Happy_path_renames_dir_csproj_refs_sln_and_slnx()
    {
        var root = CreateFixtureRoot();
        try
        {
            var oldCsproj = Path.Combine(root, "DupsFinder.Core", "DupsFinder.Core.csproj");
            var plan = ProjectRenameHelper.CreatePlan(oldCsproj, "DupFinder.Core", root);
            Assert.Equal(3, plan.TextEdits.Count);
            Assert.Contains(plan.TextEdits, e => e.Path.EndsWith("App.csproj", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.TextEdits, e => e.Path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.TextEdits, e => e.Path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));

            var result = ProjectRenameHelper.Apply(plan);
            Assert.Contains("Rename applied successfully", result, StringComparison.Ordinal);

            Assert.False(Directory.Exists(Path.Combine(root, "DupsFinder.Core")));
            Assert.True(File.Exists(Path.Combine(root, "DupFinder.Core", "DupFinder.Core.csproj")));

            var hostText = File.ReadAllText(Path.Combine(root, "App", "App.csproj"));
            Assert.Contains("DupFinder.Core", hostText, StringComparison.Ordinal);
            Assert.DoesNotContain("DupsFinder.Core", hostText, StringComparison.Ordinal);

            var sln = File.ReadAllText(Path.Combine(root, "App.sln"));
            Assert.Contains("DupFinder.Core\\DupFinder.Core.csproj", sln, StringComparison.OrdinalIgnoreCase);

            var slnx = File.ReadAllText(Path.Combine(root, "App.slnx"));
            Assert.Contains("DupFinder.Core", slnx, StringComparison.Ordinal);
            Assert.DoesNotContain("DupsFinder.Core", slnx, StringComparison.Ordinal);

            var renamedCsproj = File.ReadAllText(Path.Combine(root, "DupFinder.Core", "DupFinder.Core.csproj"));
            Assert.Contains("<AssemblyName>DupFinder.Core</AssemblyName>", renamedCsproj, StringComparison.Ordinal);
            Assert.Contains("<RootNamespace>DupFinder.Core</RootNamespace>", renamedCsproj, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Relative_ProjectReference_is_updated()
    {
        var root = CreateTempRoot();
        try
        {
            WriteSdkProject(Path.Combine(root, "Lib", "Lib.csproj"), withNameProps: false);
            Directory.CreateDirectory(Path.Combine(root, "Host"));
            File.WriteAllText(
                Path.Combine(root, "Host", "Host.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Lib\Lib.csproj" />
                  </ItemGroup>
                </Project>
                """);

            var plan = ProjectRenameHelper.CreatePlan(Path.Combine(root, "Lib", "Lib.csproj"), "Lib2", root);
            ProjectRenameHelper.Apply(plan);

            var host = File.ReadAllText(Path.Combine(root, "Host", "Host.csproj"));
            Assert.Contains(@"..\Lib2\Lib2.csproj", host, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"..\Lib\Lib.csproj", host, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void AssemblyName_not_equal_folder_is_left_unchanged()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Foo"));
            File.WriteAllText(
                Path.Combine(root, "Foo", "Foo.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <AssemblyName>Custom.Assembly</AssemblyName>
                    <RootNamespace>Custom.Root</RootNamespace>
                  </PropertyGroup>
                </Project>
                """);

            var plan = ProjectRenameHelper.CreatePlan(Path.Combine(root, "Foo", "Foo.csproj"), "Bar", root);
            Assert.False(plan.UpdateAssemblyName);
            Assert.False(plan.UpdateRootNamespace);
            ProjectRenameHelper.Apply(plan);

            var text = File.ReadAllText(Path.Combine(root, "Bar", "Bar.csproj"));
            Assert.Contains("<AssemblyName>Custom.Assembly</AssemblyName>", text, StringComparison.Ordinal);
            Assert.Contains("<RootNamespace>Custom.Root</RootNamespace>", text, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Multiple_solutions_all_updated()
    {
        var root = CreateTempRoot();
        try
        {
            WriteSdkProject(Path.Combine(root, "Core", "Core.csproj"), withNameProps: false);
            WriteClassicSln(Path.Combine(root, "One.sln"), "Core", @"Core\Core.csproj");
            WriteClassicSln(Path.Combine(root, "Two.sln"), "Core", @"Core\Core.csproj");

            var plan = ProjectRenameHelper.CreatePlan(Path.Combine(root, "Core", "Core.csproj"), "Core2", root);
            Assert.Equal(2, plan.TextEdits.Count(e => e.Path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)));
            ProjectRenameHelper.Apply(plan);

            Assert.Contains("Core2\\Core2.csproj", File.ReadAllText(Path.Combine(root, "One.sln")), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Core2\\Core2.csproj", File.ReadAllText(Path.Combine(root, "Two.sln")), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Unsupported_layout_hard_fails_without_writes()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(
                Path.Combine(root, "src", "Odd.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                </Project>
                """);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ProjectRenameHelper.CreatePlan(Path.Combine(root, "src", "Odd.csproj"), "Odd2", root));
            Assert.Contains("Unsupported layout", ex.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root, "src", "Odd.csproj")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Old_style_csproj_is_explicitly_unsupported()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Legacy"));
            File.WriteAllText(
                Path.Combine(root, "Legacy", "Legacy.csproj"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                </Project>
                """);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ProjectRenameHelper.CreatePlan(Path.Combine(root, "Legacy", "Legacy.csproj"), "Legacy2", root));
            Assert.Contains("SDK-style", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateFixtureRoot()
    {
        var root = CreateTempRoot();
        WriteSdkProject(
            Path.Combine(root, "DupsFinder.Core", "DupsFinder.Core.csproj"),
            withNameProps: true,
            assemblyAndRoot: "DupsFinder.Core");

        Directory.CreateDirectory(Path.Combine(root, "App"));
        File.WriteAllText(
            Path.Combine(root, "App", "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\DupsFinder.Core\DupsFinder.Core.csproj" />
              </ItemGroup>
            </Project>
            """);

        WriteClassicSln(Path.Combine(root, "App.sln"), "DupsFinder.Core", @"DupsFinder.Core\DupsFinder.Core.csproj");
        File.WriteAllText(
            Path.Combine(root, "App.slnx"),
            """
            <Solution>
              <Project Path="DupsFinder.Core/DupsFinder.Core.csproj" />
            </Solution>
            """);
        return root;
    }

    private static void WriteSdkProject(string path, bool withNameProps, string? assemblyAndRoot = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!withNameProps)
        {
            File.WriteAllText(path, """
                <Project Sdk="Microsoft.NET.Sdk">
                </Project>
                """);
            return;
        }

        var name = assemblyAndRoot ?? Path.GetFileNameWithoutExtension(path);
        File.WriteAllText(path, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <AssemblyName>{name}</AssemblyName>
                <RootNamespace>{name}</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
    }

    private static void WriteClassicSln(string path, string name, string relativeCsproj)
    {
        File.WriteAllText(path,
            "Microsoft Visual Studio Solution File, Format Version 12.00" + Environment.NewLine
            + $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{name}\", \"{relativeCsproj}\", \"{{11111111-1111-1111-1111-111111111111}}\""
            + Environment.NewLine
            + "EndProject"
            + Environment.NewLine);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpRenameProj", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
