using RoslynMcpServer.Services.Testing.Narrowing.Models;

namespace RoslynMcpServer.Services.Testing;

internal static class TestImpactDepth
{
    internal static string Allowed => string.Join(", ", _names);

    internal static TestClassStageLevel? Parse(string depth)
    {
        var index = Array.FindIndex(_names, name => string.Equals(name, depth, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : (TestClassStageLevel)index;
    }

    internal static string Name(TestClassStageLevel level) => _names[(int)level];

    internal static string State(TestClassStageLevel level) => $"State: selected depth {Name(level)}.";

    private static readonly string[] _names = ["changed", "direct", "di", "all"];
}
