using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

/// <summary>Isolated refusal witness; this does not establish production cache eligibility.</summary>
internal static class CapabilityOperationGate
{
    public static string Execute(
        CSharpCompilation compilation,
        string operation,
        bool stateKnown,
        Action executeHydrated,
        Action? executeOrdinary,
        Action<string> log)
    {
        var reason = Reason(compilation, operation, stateKnown);
        if (reason is not null)
        {
            var route = executeOrdinary is null ? "refusal" : "ordinary-load";
            log($"stage=operation project={compilation.AssemblyName} configuration=unknown tfm=unknown "
                + $"operation={operation} reason={reason} route={route}");
            executeOrdinary?.Invoke();
            return reason;
        }
        executeHydrated();
        return "supported-query";
    }

    private static string? Reason(CSharpCompilation compilation, string operation, bool stateKnown)
    {
        if (operation is "emit" or "sign")
        {
            return "excluded-emit-signing";
        }
        if (operation is "metadata-documentation" or "documentation-analyzer")
        {
            return "excluded-metadata-documentation";
        }
        if (!stateKnown || operation != "selected-symbols")
        {
            return "signing-state-unknown";
        }
        var options = compilation.Options;
        if (options.StrongNameProvider is not null and not DesktopStrongNameProvider)
        {
            return "signing-state-unknown";
        }
        if (!string.IsNullOrEmpty(options.CryptoKeyFile) || !string.IsNullOrEmpty(options.CryptoKeyContainer)
            || !options.CryptoPublicKey.IsDefaultOrEmpty || options.PublicSign || options.DelaySign == true)
        {
            return "signing-dependent";
        }
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            var type = attribute.AttributeClass;
            if (type is null || type.TypeKind == TypeKind.Error)
            {
                return "signing-state-unknown";
            }
            if (type.ToDisplayString() is "System.Reflection.AssemblyKeyFileAttribute"
                or "System.Reflection.AssemblyKeyNameAttribute" or "System.Reflection.AssemblyDelaySignAttribute"
                or "System.Reflection.AssemblySignatureKeyAttribute")
            {
                return "signing-dependent";
            }
            if (type.ToDisplayString() == "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
            {
                var friend = attribute.ConstructorArguments.FirstOrDefault().Value as string;
                if (friend is null || !AssemblyIdentity.TryParseDisplayName(friend, out var identity))
                {
                    return "signing-state-unknown";
                }
                if (!identity.PublicKey.IsDefaultOrEmpty || !identity.PublicKeyToken.IsDefaultOrEmpty)
                {
                    return "signing-dependent";
                }
            }
        }
        if (!compilation.Assembly.Identity.PublicKey.IsDefaultOrEmpty)
        {
            return "signing-dependent";
        }
        return null;
    }
}
