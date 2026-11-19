using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace RoslynMcpServer.Tests.SourceStructure;

public sealed class RapidTiaWorkBoundsTests
{
    [Fact]
    public void Estimate_does_not_request_full_project_diagnostics()
    {
        var body = Body("RoslynMcpServer.Services.Testing", "TestImpactEstimator", "CompilationErrorsAsync");
        var compilation = body.Analysis.Compilation.GetTypeByMetadataName("Microsoft.CodeAnalysis.Compilation");
        Assert.NotNull(compilation);
        Assert.DoesNotContain(body.ReferencesNamed("GetDiagnostics"), reference =>
            SymbolEqualityComparer.Default.Equals(reference.Symbol?.ContainingType, compilation));
    }

    [Theory]
    [InlineData("RememberNotices", "FindIndex")]
    [InlineData("RememberGap", "Exists")]
    public void Recording_an_event_does_not_scan_session_history(string member, string lookup)
    {
        var body = Body("RoslynMcpServer.Services.Models", "WorkspaceInputSession", member);
        var list = body.Analysis.Compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
        Assert.NotNull(list);
        Assert.DoesNotContain(body.ReferencesNamed(lookup), reference =>
            SymbolEqualityComparer.Default.Equals(reference.Symbol?.ContainingType.OriginalDefinition, list));
    }

    [Fact]
    public void Building_one_path_snapshot_does_not_enumerate_global_notices()
    {
        var body = Body("RoslynMcpServer.Services.Models", "WorkspaceInputSession", "PathSnapshot");
        var notices = Assert.Single(body.Method.ContainingType.GetMembers("_notices"));
        Assert.DoesNotContain(body.Nodes.OfType<ForEachStatementSyntax>(), loop =>
            SymbolEqualityComparer.Default.Equals(body.Model.GetSymbolInfo(loop.Expression).Symbol, notices));
    }

    [Fact]
    public void Checking_rename_endpoints_does_not_access_the_full_input_inventory()
    {
        var body = Body("RoslynMcpServer.Services.Workspace", "WorkspaceDiskEventClassifier", "HasMappedRenamePath");
        var map = body.Analysis.Compilation.GetTypeByMetadataName("RoslynMcpServer.Services.Models.WorkspaceInputMap");
        Assert.NotNull(map);
        var paths = Assert.Single(map.GetMembers("Paths"));
        Assert.Empty(body.ReferencesTo(paths));
    }

    private static MethodBodyScope Body(string namespaceName, string typeName, string methodName)
    {
        var analysis = ProductionAnalysis.Instance;
        Assert.Empty(analysis.InputDiagnostics);
        var diagnostics = new List<string>();
        var type = analysis.FindType(namespaceName, typeName, diagnostics);
        Assert.NotNull(type);
        var method = analysis.FindDeclaredMethod(type, methodName, diagnostics);
        Assert.Empty(diagnostics);
        Assert.NotNull(method);
        var (body, error) = MethodBodyScope.Create(analysis, method);
        Assert.Null(error);
        return Assert.IsType<MethodBodyScope>(body);
    }
}
