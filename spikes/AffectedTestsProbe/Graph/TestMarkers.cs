using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>
/// Test, lifecycle, and data-source attribute names. Custom attributes count only when their base type binds
/// to one of these roots. <c>DataRow</c> is not a test marker.
/// </summary>
internal static class TestMarkers
{
    private static readonly HashSet<string> TestAttributeRoots = new(StringComparer.Ordinal)
    {
        "Fact",
        "Theory",
        "Test",
        "TestCase",
        "TestCaseSource",
        "TestMethod",
    };

    private static readonly HashSet<string> LifecycleAttributeRoots = new(StringComparer.Ordinal)
    {
        "SetUp",
        "TearDown",
        "OneTimeSetUp",
        "OneTimeTearDown",
        "TestInitialize",
        "TestCleanup",
        "ClassInitialize",
        "ClassCleanup",
    };

    private static readonly HashSet<string> DataAttributeRoots = new(StringComparer.Ordinal)
    {
        "MemberData",
        "ClassData",
        "TestCaseSource",
        "DynamicData",
    };

    public static bool IsTestMethod(IMethodSymbol method) =>
        method.GetAttributes().Any(attribute => IsTestAttribute(attribute.AttributeClass));

    public static bool IsTestAttribute(INamedTypeSymbol? type) => Matches(type, TestAttributeRoots);

    public static bool IsLifecycleAttribute(INamedTypeSymbol? type) => Matches(type, LifecycleAttributeRoots);

    public static bool IsDataAttribute(INamedTypeSymbol? type) => Matches(type, DataAttributeRoots);

    public static string? DataAttributeName(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var name = Normalize(current.Name);
            if (DataAttributeRoots.Contains(name))
            {
                return name;
            }
        }

        return null;
    }

    private static bool Matches(INamedTypeSymbol? type, HashSet<string> roots)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (roots.Contains(Normalize(current.Name)))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string typeName)
    {
        var simple = typeName;
        var lastDot = simple.LastIndexOf('.');
        if (lastDot >= 0)
        {
            simple = simple[(lastDot + 1)..];
        }

        return simple.EndsWith("Attribute", StringComparison.Ordinal)
            ? simple[..^"Attribute".Length]
            : simple;
    }
}
