using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Host.Mef;

namespace RoslynMcpServer.Services.Editing;

internal static class RoslynCodeFixBridge
{
    internal static bool IsAvailable => _getExportsMethod is not null;

    internal static async Task<IReadOnlyList<(string Title, CodeAction Action)>> GetFixesAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var host = document.Project.Solution.Workspace.Services.HostServices;
        if (_getExportsMethod?.DeclaringType?.IsInstanceOfType(host) != true
            || _getExportsMethod.Invoke(host, null) is not IEnumerable<Lazy<CodeFixProvider>> exports)
        {
            return [];
        }

        var fixes = new List<(string Title, CodeAction Action)>();
        var language = document.Project.Language;
        var context = new CodeFixContext(document, diagnostic, (action, _) =>
        {
            lock (fixes)
            {
                fixes.Add((action.Title, action));
            }
        }, cancellationToken);

        foreach (var export in exports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = export.Value;
            var metadata = provider.GetType().GetCustomAttribute<ExportCodeFixProviderAttribute>();
            if (metadata is not null && metadata.Languages.Contains(language)
                && metadata.DocumentKinds.Contains(nameof(Document))
                && provider.FixableDiagnosticIds.Contains(diagnostic.Id))
            {
                await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
            }
        }

        return fixes;
    }

    // Reuse the workspace's MEF providers with the compiler diagnostic already computed by the caller.
    // The internal CodeFixService instead recomputes diagnostics from IDE host analyzers, which are absent here.
    // https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Features/Core/Portable/CodeFixes/Service/CodeFixService.cs
    private static readonly MethodInfo? _getExportsMethod = typeof(MefHostServices).Assembly
        .GetType("Microsoft.CodeAnalysis.Host.Mef.IMefHostExportProvider")?
        .GetMethods()
        .SingleOrDefault(method => method.Name == "GetExports" && method.GetGenericArguments().Length == 1)?
        .MakeGenericMethod(typeof(CodeFixProvider));
}
