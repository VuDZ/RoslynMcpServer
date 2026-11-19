namespace TestImpactIlProbe.Tests.Fixtures;

internal static class LambdaUser
{
    public static Func<int> Deferred() => () => LambdaTarget.Value();

    public static async Task<int> LaterAsync()
    {
        await Task.Yield();
        return AsyncTarget.Value();
    }
}
