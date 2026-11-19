namespace AffectedTestsProbe;

/// <summary>
/// Small solution for the shared edge rules. None of these production members is called directly from the test
/// method that should reach it, except the ordinary body call used as a control.
/// </summary>
internal static class PrecheckSource
{
    public const string Text =
        """
        using System.Collections.Generic;
        using Xunit;

        namespace Moq
        {
            public sealed class Mock<T> where T : class
            {
                public T Object => default!;
            }
        }

        namespace NUnit.Framework
        {
            public sealed class SetUpAttribute : System.Attribute { }
        }

        namespace Probe
        {
            public static class ProbeTarget
            {
                public static void FromFixtureCtor() { }
                public static void FromFixtureInit() { }
                public static void FromStaticCtor() { }
                public static void FromTestBody() { }
                public static void FromData() { }
                public static void FromOther() { }
                public static void FromSetup() { }
                public static void FromLeft() { }
                public static void FromRight() { }
                public static int FromFieldInit() => 1;
                public static int FromLocal() => 2;
                public static int Read() => Defaults.N;
                public static int Value(int value = Defaults.N) => value;
            }

            public static class Defaults
            {
                public const int N = 4;
            }

            public static class Touched
            {
                static Touched() { ProbeTarget.FromStaticCtor(); }
                public static void Touch() { }
            }

            public sealed class Holder
            {
                public int Value = ProbeTarget.FromFieldInit();
            }

            public sealed class GraphFixture
            {
                public GraphFixture() { ProbeTarget.FromFixtureCtor(); }
            }

            public sealed class AsyncFixture : Xunit.IAsyncLifetime
            {
                public System.Threading.Tasks.Task InitializeAsync()
                {
                    ProbeTarget.FromFixtureInit();
                    return System.Threading.Tasks.Task.CompletedTask;
                }

                public System.Threading.Tasks.Task DisposeAsync() => System.Threading.Tasks.Task.CompletedTask;
            }

            public sealed class SampleTests : IClassFixture<GraphFixture>, IClassFixture<AsyncFixture>
            {
                public SampleTests(GraphFixture graph, AsyncFixture asyncFixture) { }

                [Fact]
                public void Direct()
                {
                    Touched.Touch();
                    _ = new Holder();
                    ProbeTarget.FromTestBody();
                    _ = ProbeTarget.Read();
                    _ = ProbeTarget.Value();
                }

                [Fact]
                public void Explicit()
                {
                    _ = ProbeTarget.Value(1);
                }

                [Theory]
                [MemberData(nameof(Rows))]
                public void FromRows(int value) { }

                public static IEnumerable<object[]> Rows()
                {
                    ProbeTarget.FromData();
                    yield return new object[] { 1 };
                }
            }

            public sealed class OtherTests
            {
                [Fact]
                public void Other() { ProbeTarget.FromOther(); }

                [Fact]
                public void LocalResult()
                {
                    var result = ProbeTarget.FromLocal();
                }
            }

            public sealed class SetupTests
            {
                [global::NUnit.Framework.SetUp]
                public void Before() { ProbeTarget.FromSetup(); }

                [Fact]
                public void One() { }

                [Fact]
                public void Two() { }
            }

            public interface IProbe
            {
                void Go();
            }

            public sealed class Left : IProbe
            {
                public void Go() { ProbeTarget.FromLeft(); }
            }

            public sealed class Right : IProbe
            {
                public void Go() { ProbeTarget.FromRight(); }
            }

            public sealed class VirtualTests
            {
                [Fact]
                public void Virtual()
                {
                    IProbe probe = new Left();
                    probe.Go();
                }
            }

            public sealed class MockTests
            {
                [Fact]
                public void Mocked()
                {
                    var mock = new global::Moq.Mock<IProbe>();
                    mock.Object.Go();
                }
            }
        }
        """;
}
