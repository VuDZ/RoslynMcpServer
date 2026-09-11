using System.Reflection;

namespace RoslynMcpServer.Services;

/// <summary>
/// Exact shared host contract assemblies for in-process generator execution.
/// A <c>System.*</c> prefix or the word "Roslyn" is not a substitute for this list.
/// Generation-private copies of these contracts must not be loaded; the host
/// assembly of the same simple name is used. Version rule: the host-loaded
/// identity is authoritative; a referenced version may be older than the host.
/// </summary>
internal static class AnalyzerHostContractCatalog
{
    public static readonly IReadOnlyList<HostContractIdentity> Contracts =
    [
        new("netstandard", "cc7b13ffcd2ddd51"),
        new("mscorlib", "b77a5c561934e089"),
        new("System.Private.CoreLib", "7cec85d7bea7798e"),
        new("System.Runtime", "b03f5f7f11d50a3a"),
        new("System.Runtime.Extensions", "b03f5f7f11d50a3a"),
        new("System.Runtime.InteropServices", "b03f5f7f11d50a3a"),
        new("System.Runtime.CompilerServices.Unsafe", "b03f5f7f11d50a3a"),
        new("System.Collections", "b03f5f7f11d50a3a"),
        new("System.Collections.Concurrent", "b03f5f7f11d50a3a"),
        new("System.Collections.Immutable", "b03f5f7f11d50a3a"),
        new("System.Linq", "b03f5f7f11d50a3a"),
        new("System.Linq.Expressions", "b03f5f7f11d50a3a"),
        new("System.Memory", "cc7b13ffcd2ddd51"),
        new("System.Buffers", "cc7b13ffcd2ddd51"),
        new("System.Numerics.Vectors", "b03f5f7f11d50a3a"),
        new("System.Threading", "b03f5f7f11d50a3a"),
        new("System.Threading.Tasks", "b03f5f7f11d50a3a"),
        new("System.Threading.Tasks.Extensions", "cc7b13ffcd2ddd51"),
        new("System.Threading.Tasks.Parallel", "b03f5f7f11d50a3a"),
        new("System.Text.Encoding", "b03f5f7f11d50a3a"),
        new("System.Text.Encoding.Extensions", "b03f5f7f11d50a3a"),
        new("System.Reflection", "b03f5f7f11d50a3a"),
        new("System.Reflection.Extensions", "b03f5f7f11d50a3a"),
        new("System.Reflection.Metadata", "b03f5f7f11d50a3a"),
        new("System.Reflection.Primitives", "b03f5f7f11d50a3a"),
        new("System.IO", "b03f5f7f11d50a3a"),
        new("System.Diagnostics.Debug", "b03f5f7f11d50a3a"),
        new("System.Diagnostics.Tools", "b03f5f7f11d50a3a"),
        new("System.Globalization", "b03f5f7f11d50a3a"),
        new("System.Resources.ResourceManager", "b03f5f7f11d50a3a"),
        new("Microsoft.CodeAnalysis", "31bf3856ad364e35"),
        new("Microsoft.CodeAnalysis.CSharp", "31bf3856ad364e35"),
        new("Microsoft.CodeAnalysis.Workspaces", "31bf3856ad364e35"),
        new("Microsoft.CodeAnalysis.CSharp.Workspaces", "31bf3856ad364e35"),
    ];

    private static readonly HashSet<string> Names = new(
        Contracts.Select(c => c.SimpleName),
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> SharedHostContractSimpleNames => Names;

    public static bool IsSharedHostContract(AssemblyName name)
    {
        if (name.Name is null)
        {
            return false;
        }

        var contract = Contracts.FirstOrDefault(c =>
            string.Equals(c.SimpleName, name.Name, StringComparison.OrdinalIgnoreCase));
        if (contract.SimpleName is null)
        {
            return false;
        }

        var token = name.GetPublicKeyToken();
        if (token is null || token.Length == 0)
        {
            return true;
        }

        return string.Equals(
            Convert.ToHexString(token),
            contract.PublicKeyTokenHex,
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSharedHostContract(string? simpleName)
    {
        return !string.IsNullOrWhiteSpace(simpleName) && Names.Contains(simpleName);
    }

    internal readonly record struct HostContractIdentity(string SimpleName, string PublicKeyTokenHex);
}
