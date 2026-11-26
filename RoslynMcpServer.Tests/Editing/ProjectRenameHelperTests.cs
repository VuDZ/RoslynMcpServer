using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using RoslynMcpServer.Services.Build;
using RoslynMcpServer.Tests.Support;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Editing;

public sealed class ProjectRenameHelperTests
{
    static ProjectRenameHelperTests() => MsBuildBootstrapper.Register();

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
    public async Task Rename_preserves_reference_metadata_and_excluded_items()
    {
        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old", "Old.csproj");
            var newProject = Path.Combine(root, "New", "New.csproj");
            var app = Path.Combine(root, "App", "App.csproj");
            WriteSdkProject(oldProject, withNameProps: false);
            WriteSdkProject(Path.Combine(root, "Old", "Nested", "Excluded.csproj"), withNameProps: false);
            WriteSdkProject(Path.Combine(root, "Old", "Nested", "Removed.csproj"), withNameProps: false);
            Directory.CreateDirectory(Path.GetDirectoryName(app)!);
            File.WriteAllText(app, """
                <Project Sdk="Microsoft.NET.Sdk" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                  </PropertyGroup>
                  <Choose>
                    <When Condition="'$(Configuration)' == 'Release'">
                      <ItemGroup>
                        <ProjectReference Include="../Old/Old.csproj" />
                        <projectReference Update=" ../OLD/OLD.csproj " ReferenceOutputAssembly="false" />
                        <ProjectReference Include="../Old/Nested/Excluded.csproj" Exclude="../OLD/NESTED/EXCLUDED.csproj" />
                        <ProjectReference Include="../Old/Nested/Removed.csproj" />
                        <ProjectReference Remove="../OLD/NESTED/REMOVED.csproj" />
                      </ItemGroup>
                    </When>
                  </Choose>
                  <ProjectExtensions>
                    <ItemGroup><ProjectReference Update="$(ExtensionPath)" /></ItemGroup>
                  </ProjectExtensions>
                </Project>
                """);
            await AssertEvaluatedProjectReferenceAsync(app, oldProject);
            var before = File.ReadAllText(app);
            var tool = new ProjectTools(SolutionManagerTestFactory.Create(), NullLogger<ProjectTools>.Instance);

            var preview = await tool.RenameProject(oldProject, "New", dryRun: true, searchRoot: root);
            Assert.Contains(app, preview, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllText(app));
            Assert.True(File.Exists(oldProject));

            var result = await tool.RenameProject(oldProject, "New", dryRun: false, searchRoot: root);
            Assert.Contains("Rename applied successfully", result, StringComparison.Ordinal);
            Assert.False(File.Exists(oldProject));
            await AssertEvaluatedProjectReferenceAsync(app, newProject);

            var project = XDocument.Load(app).Root!;
            var ns = project.Name.Namespace;
            var when = project.Element(ns + "Choose")!.Element(ns + "When")!;
            Assert.Equal("'$(Configuration)' == 'Release'", (string?)when.Attribute("Condition"));
            var paths = when.Element(ns + "ItemGroup")!.Elements()
                .SelectMany(item => item.Attributes().Where(attribute =>
                    attribute.Name.LocalName is "Include" or "Update" or "Remove" or "Exclude"))
                .Select(attribute => (attribute.Name.LocalName, attribute.Value));
            Assert.Equal(
                [
                    ("Include", "../New/New.csproj"),
                    ("Update", "../New/New.csproj"),
                    ("Include", "../New/Nested/Excluded.csproj"),
                    ("Exclude", "../New/Nested/Excluded.csproj"),
                    ("Include", "../New/Nested/Removed.csproj"),
                    ("Remove", "../New/Nested/Removed.csproj"),
                ],
                paths);
            var extensionItem = project.Element(ns + "ProjectExtensions")!
                .Element(ns + "ItemGroup")!.Element(ns + "ProjectReference")!;
            Assert.Equal("$(ExtensionPath)", (string?)extensionItem.Attribute("Update"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Rename_rejects_reference_selector_that_would_match_another_include()
    {
        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old", "Old.csproj");
            var app = Path.Combine(root, "App", "App.csproj");
            WriteSdkProject(oldProject, withNameProps: false);
            Directory.CreateDirectory(Path.GetDirectoryName(app)!);
            File.WriteAllText(app, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../Old/Old.csproj" />
                    <ProjectReference Include="../NEW/NEW.csproj" />
                    <ProjectReference Update="../OLD/OLD.csproj" ReferenceOutputAssembly="false" />
                  </ItemGroup>
                </Project>
                """);
            var before = File.ReadAllText(app);
            var tool = new ProjectTools(SolutionManagerTestFactory.Create(), NullLogger<ProjectTools>.Instance);

            foreach (var dryRun in new[] { true, false })
            {
                var result = await tool.RenameProject(oldProject, "New", dryRun, searchRoot: root);
                Assert.Contains("Ambiguous ProjectReference Update", result, StringComparison.Ordinal);
                Assert.Equal(before, File.ReadAllText(app));
                Assert.True(File.Exists(oldProject));
                Assert.False(Directory.Exists(Path.Combine(root, "New")));
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Rename_rejects_target_remove_whose_item_matching_could_change()
    {
        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old", "Old.csproj");
            var app = Path.Combine(root, "App", "App.csproj");
            WriteSdkProject(oldProject, withNameProps: false);
            Directory.CreateDirectory(Path.GetDirectoryName(app)!);
            var remove = new XElement("ProjectReference", new XAttribute("Remove", oldProject));
            File.WriteAllText(app, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Old/Old.csproj" ReferenceOutputAssembly="false" />
                  </ItemGroup>
                  <Target Name="FilterReferences">
                    <ItemGroup>
                      {remove}
                    </ItemGroup>
                  </Target>
                </Project>
                """);
            await AssertEvaluatedProjectReferenceAsync(app, oldProject, target: "FilterReferences");
            var before = File.ReadAllText(app);
            var tool = new ProjectTools(SolutionManagerTestFactory.Create(), NullLogger<ProjectTools>.Instance);

            foreach (var dryRun in new[] { true, false })
            {
                var result = await tool.RenameProject(oldProject, "New", dryRun, searchRoot: root);
                Assert.Contains("Unsupported target-time ProjectReference Remove", result, StringComparison.Ordinal);
                Assert.Equal(before, File.ReadAllText(app));
                Assert.True(File.Exists(oldProject));
                Assert.False(Directory.Exists(Path.Combine(root, "New")));
            }
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

    [Theory]
    [InlineData("Project", "BuildDependency")]
    [InlineData("pRoJeCt", "bUiLdDePeNdEnCy")]
    public async Task Rename_preserves_slnx_build_dependency(string projectElement, string dependencyElement)
    {
        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old", "Old.csproj");
            var newProject = Path.Combine(root, "New", "New.csproj");
            var consumer = Path.Combine(root, "Consumer", "Consumer.csproj");
            var other = Path.Combine(root, "Other", "Other.csproj");
            WriteSdkProject(oldProject, withNameProps: false);
            WriteSdkProject(consumer, withNameProps: false);
            WriteSdkProject(other, withNameProps: false);
            var solution = Path.Combine(root, "Demo.slnx");
            File.WriteAllText(solution, $"""
                <Solution>
                  <{projectElement} Path="Old/Old.csproj" />
                  <{projectElement} Path="Other/Other.csproj" />
                  <{projectElement} Path="Consumer/Consumer.csproj">
                    <{dependencyElement} Project="Old/Old.csproj" />
                    <{dependencyElement} Project="Other/Other.csproj" />
                  </{projectElement}>
                </Solution>
                """);
            var originalBytes = File.ReadAllBytes(solution);
            await AssertSolutionOpensAsync(solution, oldProject, consumer, other);

            var tool = new ProjectTools(SolutionManagerTestFactory.Create(), NullLogger<ProjectTools>.Instance);
            var preview = await tool.RenameProject(oldProject, "New", dryRun: true, searchRoot: root);
            Assert.Contains(solution, preview, StringComparison.Ordinal);
            Assert.Equal(originalBytes, File.ReadAllBytes(solution));
            Assert.True(File.Exists(oldProject));
            Assert.False(Directory.Exists(Path.Combine(root, "New")));

            var result = await tool.RenameProject(oldProject, "New", dryRun: false, searchRoot: root);
            Assert.Contains("Rename applied successfully", result, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, "Old")));
            var projects = XDocument.Load(solution).Root!.Elements(projectElement)
                .ToDictionary(project => project.Attribute("Path")!.Value);
            Assert.Contains("New/New.csproj", projects.Keys);
            var dependencies = projects["Consumer/Consumer.csproj"].Elements(dependencyElement)
                .Select(dependency => dependency.Attribute("Project")!.Value);
            Assert.Equal(["New/New.csproj", "Other/Other.csproj"], dependencies);
            await AssertSolutionOpensAsync(solution, newProject, consumer, other);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Rename_updates_only_slnx_model_projects_and_their_direct_dependencies()
    {
        static string SolutionXml(string projectPath, string dependencyPath) => $"""
            <sOlution xmlns="urn:example" xmlns:ext="urn:example" >
              <fOldEr Name="/Group/Subgroup/">
                <!-- Preserve the project note. -->
                <pRoject ext:Label='Keep é 🌱'   Path = '{projectPath}' >
                  <ext:Project  Path = 'Old/Old.csproj' />
                  <Metadata Data='&#x4F;ld/Old.csproj'/>
                </pRoject>
                <Folder Name="/Group/Subgroup/Ignored/"><Project Path="Old/Old.csproj" /></Folder>
                <ext:Project Path="Old/Old.csproj" />
              </fOldEr>
              <Project Path="App/App.csproj">
                <buildDependency Project="{dependencyPath}" />
                <ext:BuildDependency Project="Old/Old.csproj" />
                <Telemetry><BuildDependency Project="Old/Old.csproj" /></Telemetry>
              </Project>
              <TelemetryExtension>
                <project Path="Old/Old.csproj" />
                <builddependency Project="Old/Old.csproj" />
              </TelemetryExtension>
            </sOlution>
            """;

        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old", "Old.csproj");
            var appProject = Path.Combine(root, "App", "App.csproj");
            var solution = Path.Combine(root, "Demo.slnx");
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            WriteSdkProject(oldProject, withNameProps: false);
            WriteSdkProject(appProject, withNameProps: false);
            File.WriteAllText(solution, SolutionXml("Old&#x2F;Old.csproj", @"oLD\Old.csproj"), encoding);
            var original = File.ReadAllBytes(solution);
            await AssertSolutionOpensAsync(solution, oldProject, appProject);

            var tool = new ProjectTools(SolutionManagerTestFactory.Create(), NullLogger<ProjectTools>.Instance);
            var preview = await tool.RenameProject(oldProject, "New", dryRun: true, searchRoot: root);
            Assert.Contains(solution, preview, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllBytes(solution));

            var result = await tool.RenameProject(oldProject, "New", dryRun: false, searchRoot: root);
            Assert.Contains("Rename applied successfully", result, StringComparison.Ordinal);
            Assert.Equal(EncodeWithPreamble(encoding, SolutionXml("New/New.csproj", @"New\New.csproj")), File.ReadAllBytes(solution));
            await AssertSolutionOpensAsync(solution, Path.Combine(root, "New", "New.csproj"), appProject);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Rename_preserves_classic_solution_bytes_outside_the_project_header()
    {
        static string Solution(string name) => """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            # Keep this note about Old\Old.csproj.
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}")   =   "NAME", "NAME\NAME.csproj", "{AA1C6F6E-E71E-4A85-BEB4-FB7F59603BAB}"
                ProjectSection(CustomProjectPayload) = preProject
                    Marker = Old\Old.csproj
                EndProjectSection
            EndProject
            Global
                GlobalSection(CustomPayload) = postSolution
                    OldPath = Old\Old.csproj
                EndGlobalSection
            EndGlobal
            """.ReplaceLineEndings("\n")
            .Replace("NAME", name, StringComparison.Ordinal);

        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old", "Old.csproj");
            var solution = Path.Combine(root, "Demo.sln");
            WriteSdkProject(oldProject, withNameProps: false);
            File.WriteAllText(solution, Solution("Old"));
            await AssertSolutionOpensAsync(solution, oldProject);

            var plan = ProjectRenameHelper.CreatePlan(oldProject, "New", root);
            Assert.Contains("Rename applied successfully", ProjectRenameHelper.Apply(plan), StringComparison.Ordinal);

            Assert.Equal(Encoding.UTF8.GetBytes(Solution("New")), File.ReadAllBytes(solution));
            await AssertSolutionOpensAsync(solution, plan.NewProjectPath);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Rename_preserves_project_bytes_outside_references_and_name_properties()
    {
        static string RenamedProject(string name) => $"""
            <Project Sdk="Microsoft.NET.Sdk" >
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <!-- Keep this text: <AssemblyName>Old</AssemblyName> -->
                <AssemblyName >{name}</AssemblyName>
                <RootNamespace>{name}</RootNamespace>
              </PropertyGroup>
              <ProjectExtensions><AssemblyName>Old</AssemblyName></ProjectExtensions>
            </Project>
            """.ReplaceLineEndings("\r\n");

        static string ConsumerProject(string reference) => $"""
            <Project Sdk='Microsoft.NET.Sdk' >
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <ProjectReference
                    Include  = '{reference}'  />
                <ProjectReference ReferenceOutputAssembly='false'   Update = "{reference}"/>
              </ItemGroup>
              <ProjectExtensions><ItemGroup><ProjectReference Include='../Old/Old.csproj'/></ItemGroup></ProjectExtensions>
            </Project>
            """.ReplaceLineEndings("\r\n");

        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old", "Old.csproj");
            var appProject = Path.Combine(root, "App", "App.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(oldProject)!);
            Directory.CreateDirectory(Path.GetDirectoryName(appProject)!);
            File.WriteAllText(oldProject, RenamedProject("&#x4F;ld"), Encoding.Unicode);
            File.WriteAllText(appProject, ConsumerProject("../Old&#x2F;Old.csproj"), Encoding.Unicode);
            await AssertEvaluatedProjectReferenceAsync(appProject, oldProject);

            var plan = ProjectRenameHelper.CreatePlan(oldProject, "New & More", root);
            Assert.Contains("Rename applied successfully", ProjectRenameHelper.Apply(plan), StringComparison.Ordinal);

            Assert.Equal(EncodeWithPreamble(Encoding.Unicode, RenamedProject("New &amp; More")), File.ReadAllBytes(plan.NewProjectPath));
            Assert.Equal(EncodeWithPreamble(Encoding.Unicode, ConsumerProject("../New &amp; More/New &amp; More.csproj")),
                File.ReadAllBytes(appProject));
            await AssertEvaluatedProjectReferenceAsync(appProject, plan.NewProjectPath);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData("Old <!-- assembly comment -->Project", "New Project", true)]
    [InlineData("Old<!-- first --> <!-- second -->Project", "OldProject", false)]
    public async Task Rename_uses_msbuild_whitespace_rules_in_split_assembly_names(
        string originalValue,
        string expectedName,
        bool updateAssemblyName)
    {
        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old Project", "Old Project.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(oldProject)!);
            File.WriteAllText(oldProject, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <AssemblyName>{{originalValue}}</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);
            var original = File.ReadAllBytes(oldProject);
            var plan = ProjectRenameHelper.CreatePlan(oldProject, "New Project", root);
            Assert.Equal(updateAssemblyName, plan.UpdateAssemblyName);

            Assert.Contains("Rename applied successfully", ProjectRenameHelper.Apply(plan), StringComparison.Ordinal);

            if (updateAssemblyName)
            {
                var project = XDocument.Load(plan.NewProjectPath);
                var assemblyName = Assert.Single(project.Descendants("AssemblyName"));
                Assert.Equal(" assembly comment ", Assert.Single(assemblyName.Nodes().OfType<XComment>()).Value);
            }
            else
            {
                Assert.Equal(original, File.ReadAllBytes(plan.NewProjectPath));
            }

            var result = await DotNetCliRunner.RunSeparatedAsync(
                $"msbuild \"{plan.NewProjectFileName}\" -nologo -getProperty:AssemblyName",
                plan.NewDirectory,
                TimeSpan.FromSeconds(30));
            Assert.True(result.ExitCode == 0, $"{result.StdOut}{Environment.NewLine}{result.StdErr}");
            Assert.Equal(expectedName, result.StdOut.TrimEnd('\r', '\n'));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Rename_preserves_literal_and_escaped_whitespace_in_unrelated_xml_attributes()
    {
        const string Metadata = "<TelemetryExtension Data=\"tab&#9;lf&#10;cr&#13;literal\tand\nline\" />";
        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old", "Old.csproj");
            var consumer = Path.Combine(root, "App.csproj");
            var solution = Path.Combine(root, "Demo.slnx");
            WriteSdkProject(oldProject, withNameProps: false);
            File.WriteAllText(consumer, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup><ProjectReference Include="Old/Old.csproj" /></ItemGroup>
                  <ProjectExtensions>{Metadata}</ProjectExtensions>
                </Project>
                """);
            File.WriteAllText(solution, $"""<Solution><Project Path="Old/Old.csproj" />{Metadata}</Solution>""");

            ProjectRenameHelper.Apply(ProjectRenameHelper.CreatePlan(oldProject, "New", root));

            AssertProjectReference(consumer, Path.Combine(root, "New", "New.csproj"));
            foreach (var path in new[] { consumer, solution })
            {
                var element = Assert.Single(XDocument.Load(path).Descendants("TelemetryExtension"));
                Assert.Equal("tab\tlf\ncr\rliteral and line", element.Attribute("Data")!.Value);
            }
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
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_rejects_modified_input_before_moving_the_project(bool modifyProject)
    {
        var root = CreateFixtureRoot();
        try
        {
            var oldProject = Path.Combine(root, "DupsFinder.Core", "DupsFinder.Core.csproj");
            var solution = Path.Combine(root, "App.sln");
            var input = modifyProject ? oldProject : solution;
            var plan = ProjectRenameHelper.CreatePlan(oldProject, "DupFinder.Core", root);
            File.AppendAllText(input, "\nNew text on disk.\n");
            var changed = File.ReadAllBytes(input);

            Assert.Throws<InvalidOperationException>(() => ProjectRenameHelper.Apply(plan));

            Assert.Equal(changed, File.ReadAllBytes(input));
            Assert.True(File.Exists(oldProject));
            Assert.False(Directory.Exists(plan.NewDirectory));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Rename_rejects_a_reference_generated_by_an_xml_entity_before_moving_the_project()
    {
        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old", "Old.csproj");
            var consumer = Path.Combine(root, "App.csproj");
            WriteSdkProject(oldProject, withNameProps: false);
            File.WriteAllText(consumer, """
                <!DOCTYPE Project [
                  <!ENTITY reference "<ProjectReference Include='Old/Old.csproj' />">
                ]>
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>&reference;</ItemGroup>
                </Project>
                """);
            var original = File.ReadAllBytes(consumer);

            var rejection = Assert.Throws<InvalidOperationException>(() => ProjectRenameHelper.CreatePlan(oldProject, "New", root));

            Assert.Contains(consumer, rejection.Message, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllBytes(consumer));
            Assert.True(File.Exists(oldProject));
            Assert.False(Directory.Exists(Path.Combine(root, "New")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public void Rename_rejects_a_duplicate_solution_project_name_before_moving_the_project(string extension)
    {
        var root = CreateTempRoot();
        try
        {
            var oldProject = Path.Combine(root, "Old", "Old.csproj");
            var otherProject = Path.Combine(root, "Shared", "New.csproj");
            var solution = Path.Combine(root, "Demo" + extension);
            WriteSdkProject(oldProject, withNameProps: false);
            WriteSdkProject(otherProject, withNameProps: false);
            WriteGraphSolution(solution, "Old/Old.csproj", "Shared/New.csproj");
            var originalProject = File.ReadAllBytes(oldProject);
            var originalOther = File.ReadAllBytes(otherProject);
            var originalSolution = File.ReadAllBytes(solution);

            var rejection = Assert.Throws<InvalidOperationException>(() => ProjectRenameHelper.CreatePlan(oldProject, "New", root));

            Assert.Contains(solution, rejection.Message, StringComparison.Ordinal);
            Assert.Equal(originalProject, File.ReadAllBytes(oldProject));
            Assert.Equal(originalOther, File.ReadAllBytes(otherProject));
            Assert.Equal(originalSolution, File.ReadAllBytes(solution));
            Assert.False(Directory.Exists(Path.Combine(root, "New")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData(true, "Include", "$(ProjectRoot)/Core/Core.csproj")]
    [InlineData(false, "Include", "$(ProjectRoot)/Core/Core.csproj")]
    [InlineData(false, "Include", "$(ProjectPath)")]
    [InlineData(false, "Update", "Core/$(ReferencedProject).csproj")]
    [InlineData(false, "Remove", "Core/*.csproj")]
    [InlineData(false, "Exclude", "Core/*.csproj")]
    public void Computed_reference_is_rejected_during_planning(bool ownerMoves, string operation, string referencePath)
    {
        var root = CreateTempRoot();
        try
        {
            var project = Path.Combine(root, "Core", "Core.csproj");
            var owner = ownerMoves ? Path.Combine(root, "Core", "Nested", "Nested.csproj") : Path.Combine(root, "Outside.csproj");
            WriteSdkProject(project, withNameProps: false);
            Directory.CreateDirectory(Path.GetDirectoryName(owner)!);
            var include = operation == "Exclude" ? """Include="Core/Core.csproj" """ : string.Empty;
            File.WriteAllText(owner, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference {include}{operation}="{referencePath}" />
                  </ItemGroup>
                </Project>
                """);
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

    private static async Task AssertEvaluatedProjectReferenceAsync(
        string projectPath,
        string expectedTarget,
        string? target = null)
    {
        var targetArgument = target is null ? string.Empty : $" -target:{target}";
        var result = await DotNetCliRunner.RunSeparatedAsync(
            $"msbuild \"{Path.GetFileName(projectPath)}\" -nologo -p:Configuration=Release -getItem:ProjectReference{targetArgument}",
            Path.GetDirectoryName(projectPath)!,
            TimeSpan.FromSeconds(30));
        Assert.True(result.ExitCode == 0, $"{result.StdOut}{Environment.NewLine}{result.StdErr}");
        using var evaluated = JsonDocument.Parse(result.StdOut);
        var item = Assert.Single(evaluated.RootElement.GetProperty("Items").GetProperty("ProjectReference").EnumerateArray());
        Assert.Equal(expectedTarget, item.GetProperty("FullPath").GetString());
        Assert.Equal("false", item.GetProperty("ReferenceOutputAssembly").GetString());
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

    private static byte[] EncodeWithPreamble(Encoding encoding, string text) =>
        [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

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
