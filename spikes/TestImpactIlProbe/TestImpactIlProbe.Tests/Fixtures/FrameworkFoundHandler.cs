using Xunit.Abstractions;

namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>Implements an interface of a package, as a controller or a hosted service does.</summary>
internal sealed class FrameworkFoundHandler : ITestOutputHelper
{
    public void WriteLine(string message)
    {
    }

    public void WriteLine(string format, params object[] args)
    {
    }
}
