using System.Diagnostics;
using System.Runtime.InteropServices;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp.Syntax;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Services.Resolve;
using RoslynMcpServer.Tests.SourceStructure;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.AssemblyResolve;

public sealed class RuntimeAsyncDecompilationTests
{
    [Fact]
    public async Task Runtime_async_process_method_recovers_awaits_and_reports_invalid_throw()
    {
        var result = await DecompileProcessMethod(nameof(Process.WaitForExitAsync));

        Assert.True(
            result.Contains("public async Task WaitForExitAsync(", StringComparison.Ordinal),
            $"{RuntimeInformation.FrameworkDescription}, ICSharpCode.Decompiler {typeof(DecompilerSettings).Assembly.GetName().Version}:{Environment.NewLine}{result}");
        Assert.Contains("await tcs.Task.ConfigureAwait(", result, StringComparison.Ordinal);
        Assert.Contains("await WaitUntilOutputEOF(", result, StringComparison.Ordinal);
        Assert.DoesNotContain("AsyncHelpers.", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Stack underflow", result, StringComparison.Ordinal);
        Assert.StartsWith("// Incomplete decompilation", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Synchronous_process_method_keeps_its_body()
    {
        var result = await DecompileProcessMethod(nameof(Process.Refresh));

        Assert.Contains("public void Refresh()", result, StringComparison.Ordinal);
        Assert.Contains("_exited = false;", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Incomplete decompilation", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Error_expression_marks_the_rendered_code_incomplete()
    {
        var syntaxTree = new SyntaxTree();
        syntaxTree.Members.Add(new MethodDeclaration
        {
            Name = "MissingReturnValue",
            ReturnType = new PrimitiveType("int"),
            Body = new BlockStatement { new ReturnStatement(new ErrorExpression()) },
        });

        var session = DecompilerHost.Create(typeof(Process).Assembly.Location);
        var result = session.FormatCode(syntaxTree);

        Assert.StartsWith("// Incomplete decompilation", result, StringComparison.Ordinal);
        Assert.Contains("int MissingReturnValue()", result, StringComparison.Ordinal);
    }

    private static Task<string> DecompileProcessMethod(string methodName)
    {
        var processType = typeof(Process);
        var tools = CreateTools();
        return tools.GetDecompiledMethodBody(
            assemblyPath: processType.Assembly.Location,
            fullTypeName: processType.FullName!,
            methodName: methodName);
    }

    private static CodeAnalysisTools CreateTools() =>
        new(SolutionManagerTestFactory.Create(), NullLogger<CodeAnalysisTools>.Instance);
}
