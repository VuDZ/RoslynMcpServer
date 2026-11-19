namespace TestImpactIlProbe.Tests.Fixtures;

internal static class AssemblyScanner
{
    public static void Register() => Registry.Scan(typeof(OrderConsumer).Assembly);
}
