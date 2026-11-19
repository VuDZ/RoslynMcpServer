using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>
/// Runtime class name used by VSTest. A generic class whose closed name was not measured returns null;
/// the open form is not substituted.
/// </summary>
internal static class RuntimeNames
{
    public static string? TryName(INamedTypeSymbol type, out string? reason)
    {
        var attributes = new List<AttributeData>();
        for (var owner = type; owner is not null; owner = owner.BaseType)
        {
            attributes.AddRange(owner.GetAttributes());
        }

        var fixtures = attributes.Where(attribute => IsNUnit(attribute, "TestFixtureAttribute")).ToArray();
        var source = attributes.Any(attribute => IsNUnit(attribute, "TestFixtureSourceAttribute"));
        var values = fixtures.Select(attribute => attribute.ConstructorArguments
            .SelectMany(value => value.Kind == TypedConstantKind.Array ? value.Values : [value])
            .ToArray()).ToArray();
        var arguments = source || values.Any(items => items.Length > type.Arity
            || items.Any(item => item.Kind != TypedConstantKind.Type));
        if (arguments)
        {
            reason = "Runtime fixture constructor or source arguments require the whole project.";
            return null;
        }

        var name = Display(type);
        if (type.Arity == 0 && !HasGenericContainer(type))
        {
            reason = null;
            return name;
        }

        if (type.Arity == 1 && type.ContainingType is null && values.Length == 1 && values[0].Length == 1
            && values[0][0].Value is ITypeSymbol { SpecialType: SpecialType.System_Int32 })
        {
            reason = null;
            return name + "<Int32>";
        }

        reason = "Unknown or unmeasured closed generic runtime identity.";
        return null;
    }

    public static string Display(INamedTypeSymbol type) => type.ContainingType is { } parent
        ? Display(parent) + "+" + type.Name
        : (type.ContainingNamespace is null || type.ContainingNamespace.IsGlobalNamespace
            ? ""
            : type.ContainingNamespace.ToDisplayString() + ".") + type.Name;

    private static bool IsNUnit(AttributeData attribute, string name) =>
        attribute.AttributeClass is { } type
        && type.Name == name
        && type.ContainingNamespace.ToDisplayString() == "NUnit.Framework";

    private static bool HasGenericContainer(INamedTypeSymbol type) =>
        type.ContainingType is { } parent && (parent.Arity > 0 || HasGenericContainer(parent));
}
