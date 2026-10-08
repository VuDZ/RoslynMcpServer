using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynMcpServer.Services.Workspace.StateCache;

/// <summary>
/// Decides whether one loaded compilation fits the signing-independent capture profile.
/// A refusal is an unsupported capture result. This does not log a request route, emit, or sign.
/// </summary>
internal static class SnapshotSigningProfile
{
    public const string IndependentPolicy = "H-002-signing-independent-empty-search-paths";

    public static string? UnsupportedReason(CSharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var options = compilation.Options;
        if (options.StrongNameProvider is not null and not DesktopStrongNameProvider)
        {
            return "signing-state-unknown";
        }

        if (!string.IsNullOrEmpty(options.CryptoKeyFile)
            || !string.IsNullOrEmpty(options.CryptoKeyContainer)
            || !options.CryptoPublicKey.IsDefaultOrEmpty
            || options.PublicSign
            || options.DelaySign == true)
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
                or "System.Reflection.AssemblyKeyNameAttribute"
                or "System.Reflection.AssemblyDelaySignAttribute"
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
