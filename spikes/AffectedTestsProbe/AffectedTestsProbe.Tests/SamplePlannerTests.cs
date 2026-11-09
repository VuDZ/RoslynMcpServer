using Xunit;

namespace AffectedTestsProbe.Tests;

public sealed class SamplePlannerTests
{
    [Fact]
    public async Task FrameB_ClassifiesFromSourceBeforeAnyCallGraph()
    {
        using var session = ProbeSession.Open(
            """
            using Xunit;
            namespace Probe;
            public class Base { public virtual void Go() {} }
            public sealed class A : Base { public override void Go() {} }
            public sealed class B : Base { public override void Go() {} }
            public interface IFace { void M(); }
            public sealed class Impl : IFace { public void M() {} }
            public sealed class Other : IFace { public void M() {} }
            public sealed class Library
            {
                public void Uncalled() {}
                public static void Static() {}
            }
            public sealed class Tests
            {
                [Fact]
                public void Call()
                {
                    IFace face = new Impl();
                    face.M();
                    Base value = new A();
                    value.Go();
                }
            }
            """);
        var map = new ProjectMap(session.Solution, "unspecified", "unspecified");
        var first = await SamplePlanner.FrameBAsync(session.Solution, map, [], seed: 1, CancellationToken.None);
        var second = await SamplePlanner.FrameBAsync(session.Solution, map, [], seed: 1, CancellationToken.None);

        Assert.Equal(first.Hash, second.Hash);
        Assert.Contains(first.Ordinary, member => member.DocumentationId.EndsWith(".Uncalled", StringComparison.Ordinal));
        Assert.Contains(first.Ordinary, member => member.DocumentationId.EndsWith("Impl.M", StringComparison.Ordinal));
        Assert.Contains(first.Di, member => member.DocumentationId.EndsWith("Other.M", StringComparison.Ordinal));
        Assert.DoesNotContain(first.Di, member => member.DocumentationId.EndsWith("Impl.M", StringComparison.Ordinal));
        Assert.Contains(first.Static, member => member.DocumentationId.EndsWith(".Static", StringComparison.Ordinal));
        Assert.Contains(first.Virtual, member => member.DocumentationId.EndsWith("Base.Go", StringComparison.Ordinal));
        Assert.Contains(first.Virtual, member => member.DocumentationId.EndsWith("A.Go", StringComparison.Ordinal));
        Assert.Contains(first.Virtual, member => member.DocumentationId.EndsWith("B.Go", StringComparison.Ordinal));
    }
}
