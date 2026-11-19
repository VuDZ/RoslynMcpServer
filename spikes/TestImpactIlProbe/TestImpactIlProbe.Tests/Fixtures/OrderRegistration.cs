namespace TestImpactIlProbe.Tests.Fixtures;

internal static class OrderRegistration
{
    public static void Register() => Registry.Add<IOrderService, OrderService>();
}
