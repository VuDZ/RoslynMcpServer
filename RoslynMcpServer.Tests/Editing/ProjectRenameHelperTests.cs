using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Services;
using RoslynMcpServer.Services.Build;
using RoslynMcpServer.Tests.Support;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Editing;

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
    public void Slnx_backslash_project_path_is_updated()
    {
        var root = CreateTempRoot();
        try
        {
            WriteSdkProject(Path.Combine(root, "Core", "Core.csproj"), withNameProps: false);
            File.WriteAllText(
                Path.Combine(root, "App.slnx"),
                """
                <Solution>
                  <Project Path="Core\Core.csproj" />
                </Solution>
                """);

            var plan = ProjectRenameHelper.CreatePlan(Path.Combine(root, "Core", "Core.csproj"), "Core2", root);
            ProjectRenameHelper.Apply(plan);

            var slnx = File.ReadAllText(Path.Combine(root, "App.slnx"));
            Assert.Contains("Core2", slnx, StringComparison.Ordinal);
            Assert.DoesNotContain("Core\\Core.csproj", slnx, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Rename_preserves_slnx_build_dependency()
    {
        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old", "Old.csproj");
            var newProject = Path.Combine(root, "New", "New.csproj");
            var consumer = Path.Combine(root, "Consumer", "Consumer.csproj");
            WriteSdkProject(oldProject, withNameProps: false);
            WriteSdkProject(consumer, withNameProps: false);
            var solution = Path.Combine(root, "Demo.slnx");
            const string OriginalSolution = """
                <Solution>
                  <Project Path="Old/Old.csproj" />
                  <Project Path="Consumer/Consumer.csproj">
                    <BuildDependency Project="Old/Old.csproj" />
                  </Project>
                </Solution>
                """;
            File.WriteAllText(solution, OriginalSolution);
            await AssertSolutionOpensAsync(solution, oldProject, consumer);

            var tool = new ProjectTools(SolutionManagerTestFactory.Create(), NullLogger<ProjectTools>.Instance);
            var preview = await tool.RenameProject(oldProject, "New", dryRun: true, searchRoot: root);
            Assert.Contains(solution, preview, StringComparison.Ordinal);
            Assert.Equal(OriginalSolution, File.ReadAllText(solution));
            Assert.True(File.Exists(oldProject));
            Assert.False(Directory.Exists(Path.Combine(root, "New")));

            var result = await tool.RenameProject(oldProject, "New", dryRun: false, searchRoot: root);
            Assert.Contains("Rename applied successfully", result, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, "Old")));
            var projects = XDocument.Load(solution).Root!.Elements("Project")
                .ToDictionary(project => project.Attribute("Path")!.Value);
            Assert.Contains("New/New.csproj", projects.Keys);
            var dependency = Assert.Single(projects["Consumer/Consumer.csproj"].Elements("BuildDependency"));
            Assert.Equal("New/New.csproj", dependency.Attribute("Project")!.Value);
            await AssertSolutionOpensAsync(solution, newProject, consumer);
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

    [Theory]
    [InlineData(".sln", "Nested.csproj", "Outside.csproj")]
    [InlineData(".slnx", "Nested.fsproj", "Outside.vbproj")]
    public async Task Preview_and_apply_use_the_final_graph_paths(
        string extension,
        string nestedProjectFileName,
        string outsideProjectFileName)
    {
        var root = CreateTempRoot();
        try
        {
            var oldDirectory = Path.Combine(root, "Core");
            var oldProject = Path.Combine(oldDirectory, "Core.csproj");
            var outsideDirectory = Path.Combine(root, "Outside");
            var outsideProject = Path.Combine(outsideDirectory, outsideProjectFileName);
            WriteSdkProject(oldProject, withNameProps: true);
            WriteProjectWithReferences(
                Path.Combine(oldDirectory, "Nested", nestedProjectFileName),
                "../../Core/Core.csproj");
            WriteProjectWithReferences(outsideProject, $@"..\Core\Nested\{nestedProjectFileName}");
            var nestedSolutionPath = $"../Core/Nested/{nestedProjectFileName}";
            WriteGraphSolution(Path.Combine(oldDirectory, "Inside" + extension), "Core.csproj", nestedSolutionPath);
            var outsideSolution = Path.Combine(outsideDirectory, "Outside" + extension);
            WriteGraphSolution(outsideSolution, nestedSolutionPath, outsideProjectFileName);

            var plan = ProjectRenameHelper.CreatePlan(oldProject, "Renamed", outsideDirectory);
            var preview = ProjectRenameHelper.FormatPlan(plan, dryRun: true);
            var nestedProject = Path.Combine(plan.NewDirectory, "Nested", nestedProjectFileName);
            var insideSolution = Path.Combine(plan.NewDirectory, "Inside" + extension);
            Assert.Equal(4, plan.TextEdits.Count);
            Assert.All(plan.TextEdits, edit => Assert.Contains(edit.Path, preview, StringComparison.Ordinal));
            Assert.Contains(insideSolution, preview, StringComparison.Ordinal);
            Assert.Contains(nestedProject, preview, StringComparison.Ordinal);
            Assert.True(File.Exists(oldProject));
            Assert.False(Directory.Exists(plan.NewDirectory));

            Assert.Contains("Rename applied successfully", ProjectRenameHelper.Apply(plan), StringComparison.Ordinal);
            Assert.False(Directory.Exists(oldDirectory));
            Assert.All(plan.TextEdits, edit => Assert.Equal(edit.NewContent, File.ReadAllText(edit.Path)));
            AssertProjectReference(nestedProject, plan.NewProjectPath);
            AssertProjectReference(outsideProject, nestedProject);
            await AssertSolutionOpensAsync(insideSolution, plan.NewProjectPath, nestedProject);
            await AssertSolutionOpensAsync(outsideSolution, nestedProject, outsideProject);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData(false, 1200, "utf-16")]
    [InlineData(true, 1200, "utf-16")]
    [InlineData(false, 12000, "utf-32")]
    [InlineData(true, 12000, "utf-32")]
    [InlineData(false, 12001, "utf-32BE")]
    [InlineData(true, 12001, "utf-32BE")]
    public async Task Rename_preserves_bom_in_moved_project_and_solution(
        bool trackedWrite,
        int codePage,
        string declaredEncoding)
    {
        var root = CreateTempRoot();
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "Core")).FullName;
            var project = Path.Combine(directory, "Core.csproj");
            var encoding = Encoding.GetEncoding(codePage);
            File.WriteAllText(
                project,
                $"""
                <?xml version="1.0" encoding="{declaredEncoding}"?>
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <AssemblyName>Core</AssemblyName>
                  </PropertyGroup>
                </Project>
                """,
                encoding);
            File.WriteAllText(
                Path.Combine(directory, "App.slnx"),
                $"""
                <?xml version="1.0" encoding="{declaredEncoding}"?>
                <Solution>
                  <Project Path="Core.csproj" />
                </Solution>
                """,
                encoding);

            Assert.NotNull(XDocument.Load(project).Root);
            Assert.NotNull(XDocument.Load(Path.Combine(directory, "App.slnx")).Root);

            string result;
            if (trackedWrite)
            {
                var tool = new ProjectTools(SolutionManagerTestFactory.Create(), NullLogger<ProjectTools>.Instance);
                result = await tool.RenameProject(project, "Renamed", dryRun: false, searchRoot: root);
            }
            else
            {
                result = ProjectRenameHelper.Apply(ProjectRenameHelper.CreatePlan(project, "Renamed", root));
            }

            Assert.Contains("Rename applied successfully", result, StringComparison.Ordinal);
            var newDirectory = Path.Combine(root, "Renamed");
            var newProject = Path.Combine(newDirectory, "Renamed.csproj");
            var newSolution = Path.Combine(newDirectory, "App.slnx");
            Assert.True(File.ReadAllBytes(newProject).AsSpan().StartsWith(encoding.Preamble));
            Assert.True(File.ReadAllBytes(newSolution).AsSpan().StartsWith(encoding.Preamble));
            Assert.Equal("Renamed", Assert.Single(XDocument.Load(newProject).Descendants("AssemblyName")).Value);
            var solutionProject = Assert.Single(XDocument.Load(newSolution).Descendants("Project"));
            Assert.Equal("Renamed.csproj", solutionProject.Attribute("Path")!.Value);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData(true, 28591, "iso-8859-1")]
    [InlineData(false, 28591, "iso-8859-1")]
    [InlineData(false, 12001, "utf-32")]
    public void Unsupported_xml_encoding_is_rejected_before_move(
        bool projectFile,
        int codePage,
        string declaredEncoding)
    {
        var root = CreateTempRoot();
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "Core")).FullName;
            var project = Path.Combine(directory, "Core.csproj");
            var solution = Path.Combine(directory, "App.slnx");
            var unsupportedPath = projectFile ? project : solution;
            var encoding = Encoding.GetEncoding(codePage);
            if (projectFile)
            {
                File.WriteAllText(
                    project,
                    $"""
                    <?xml version="1.0" encoding="{declaredEncoding}"?>
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <AssemblyName>Core</AssemblyName>
                      </PropertyGroup>
                    </Project>
                    """,
                    encoding);
            }
            else
            {
                WriteSdkProject(project, withNameProps: false);
                File.WriteAllText(
                    solution,
                    $"""
                    <?xml version="1.0" encoding="{declaredEncoding}"?>
                    <Solution>
                      <Project Path="Core.csproj" />
                    </Solution>
                    """,
                    encoding);
            }

            var original = File.ReadAllBytes(unsupportedPath);
            var error = Assert.Throws<InvalidOperationException>(() => ProjectRenameHelper.CreatePlan(project, "Renamed", root));
            Assert.Contains("Unsupported XML encoding", error.Message, StringComparison.Ordinal);
            Assert.Contains(unsupportedPath, error.Message, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllBytes(unsupportedPath));
            Assert.False(Directory.Exists(Path.Combine(root, "Renamed")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData(".git", false, "Git checkout root")]
    [InlineData("Renamed.csproj", false, "Destination project path already exists")]
    [InlineData("Renamed.csproj", true, "Destination project path already exists")]
    public void Unsupported_moves_leave_the_original_directory_untouched(
        string conflictingName,
        bool conflictAfterPlanning,
        string reason)
    {
        var root = CreateTempRoot();
        try
        {
            var oldDirectory = Path.Combine(root, "Core");
            var oldProject = Path.Combine(oldDirectory, "Core.csproj");
            WriteSdkProject(oldProject, withNameProps: false);
            var original = File.ReadAllText(oldProject);
            var plan = conflictAfterPlanning ? ProjectRenameHelper.CreatePlan(oldProject, "Renamed", root) : null;
            var conflict = Path.Combine(oldDirectory, conflictingName);
            File.WriteAllText(conflict, "Existing content.");

            var error = Assert.Throws<InvalidOperationException>(() =>
            {
                if (plan is null)
                {
                    ProjectRenameHelper.CreatePlan(oldProject, "Renamed", root);
                }
                else
                {
                    ProjectRenameHelper.Apply(plan);
                }
            });
            Assert.Contains(reason, error.Message, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllText(oldProject));
            Assert.Equal("Existing content.", File.ReadAllText(conflict));
            Assert.False(Directory.Exists(Path.Combine(root, "Renamed")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData(true, "$(ProjectRoot)/Core/Core.csproj")]
    [InlineData(false, "$(ProjectRoot)/Core/Core.csproj")]
    [InlineData(false, "$(ProjectPath)")]
    public void Computed_reference_is_rejected_during_planning(bool ownerMoves, string referencePath)
    {
        var root = CreateTempRoot();
        try
        {
            var project = Path.Combine(root, "Core", "Core.csproj");
            var owner = ownerMoves ? Path.Combine(root, "Core", "Nested", "Nested.csproj") : Path.Combine(root, "Outside.csproj");
            WriteSdkProject(project, withNameProps: false);
            WriteProjectWithReferences(owner, referencePath);
            var originalOwner = File.ReadAllText(owner);

            var error = Assert.Throws<InvalidOperationException>(() => ProjectRenameHelper.CreatePlan(project, "Renamed", root));
            Assert.Contains("Unsupported ProjectReference path", error.Message, StringComparison.Ordinal);
            Assert.Contains(owner, error.Message, StringComparison.Ordinal);
            Assert.Equal(originalOwner, File.ReadAllText(owner));
            Assert.True(File.Exists(project));
            Assert.False(Directory.Exists(Path.Combine(root, "Renamed")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static void WriteProjectWithReferences(string path, string referencePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="{referencePath}" />
              </ItemGroup>
            </Project>
            """);
    }

    private static void WriteGraphSolution(string path, params string[] projects)
    {
        if (Path.GetExtension(path) == ".slnx")
        {
            var entries = projects.Select(project => new XElement("Project", new XAttribute("Path", project)));
            new XElement("Solution", entries).Save(path);
            return;
        }

        var lines = new List<string> { "Microsoft Visual Studio Solution File, Format Version 12.00" };
        foreach (var project in projects)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var solutionPath = project.Replace('/', '\\');
            lines.Add($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{name}\", \"{solutionPath}\", \"{Guid.NewGuid():B}\"");
            lines.Add("EndProject");
        }

        File.WriteAllLines(path, lines);
    }

    private static void AssertProjectReference(string projectPath, string expectedTarget)
    {
        var reference = Assert.Single(XDocument.Load(projectPath).Descendants("ProjectReference"));
        var include = reference.Attribute("Include")!.Value.Replace('\\', '/');
        Assert.Equal(expectedTarget, Path.GetFullPath(include, Path.GetDirectoryName(projectPath)!));
        Assert.True(File.Exists(expectedTarget), $"ProjectReference target does not exist: {expectedTarget}");
    }

    private static async Task AssertSolutionOpensAsync(string solutionPath, params string[] projects)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        var (exitCode, output) = await DotNetCliRunner.RunAsync(
            $"sln \"{Path.GetFileName(solutionPath)}\" list",
            directory,
            CancellationToken.None,
            TimeSpan.FromSeconds(30));
        Assert.True(exitCode == 0, output);
        var projectList = output.Replace('\\', '/');
        Assert.All(projects, project =>
        {
            Assert.True(File.Exists(project), $"Solution project does not exist: {project}");
            var relativePath = Path.GetRelativePath(directory, project).Replace('\\', '/');
            Assert.Contains(relativePath, projectList, StringComparison.Ordinal);
        });
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
