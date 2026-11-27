using System.Reflection;
using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.Support;

/// <summary>Finds the test methods of this assembly that carry a <see cref="TestCategories"/> trait.</summary>
internal static class TestCategoryInventory
{
    /// <returns>Every test method of the assembly, facts and theories alike.</returns>
    public static IEnumerable<MethodInfo> TestMethods()
    {
        // GetTypes loads every test type, including ones that mention MSBuild. The reference
        // identity is Microsoft.Build.Framework 15.1.0.0, which is not next to the test dll.
        // Without the locator the load fails when this test runs before any other registration.
        MsBuildBootstrapper.Register();
        return typeof(TestCategoryInventory).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<FactAttribute>(inherit: true).Any());
    }

    /// <returns>"Type.Method" of every test method in <paramref name="category"/>, sorted.</returns>
    public static string[] Names(string category) =>
        TestMethods()
            .Where(method => HasCategory(method, category))
            .Select(Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Whether the method or its type carries the category trait.</summary>
    public static bool HasCategory(MethodInfo method, string category) =>
        method.GetCustomAttributesData()
            .Concat(method.DeclaringType!.GetCustomAttributesData())
            .Any(attribute => IsCategory(attribute, category));

    public static string Name(MethodInfo method) => method.DeclaringType!.Name + "." + method.Name;

    // xUnit v2 TraitAttribute exposes its name and value only as constructor arguments.
    private static bool IsCategory(CustomAttributeData attribute, string category) =>
        attribute.AttributeType == typeof(TraitAttribute)
        && attribute.ConstructorArguments is [{ Value: "Category" }, { Value: string value }]
        && value == category;
}
