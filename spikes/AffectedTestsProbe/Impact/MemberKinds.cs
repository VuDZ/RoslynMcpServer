using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>A coarse member kind for the anonymized summary. It carries no names.</summary>
internal static class MemberKinds
{
    public static string Of(ISymbol symbol)
    {
        var owner = symbol is IMethodSymbol { AssociatedSymbol: { } associated } ? associated : symbol;
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor })
        {
            return "constructor";
        }

        if (owner.IsStatic)
        {
            return "static";
        }

        if (owner.IsOverride || owner.IsVirtual || owner.IsAbstract)
        {
            return "virtual";
        }

        if (ImplementsInterface(owner))
        {
            return "interface";
        }

        return symbol is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet } ? "accessor" : "ordinary";
    }

    private static bool ImplementsInterface(ISymbol member)
    {
        var type = member.ContainingType;
        if (type is null)
        {
            return false;
        }

        foreach (var contract in type.AllInterfaces)
        {
            foreach (var candidate in contract.GetMembers())
            {
                if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(candidate), member))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
