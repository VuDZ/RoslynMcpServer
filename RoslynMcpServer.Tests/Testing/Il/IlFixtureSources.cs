namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>
/// Sources of the fixture assemblies: small types, one per rule of the dependency graph. The cases follow the
/// tests and fixtures of the TestImpactIlProbe spike.
/// </summary>
internal static class IlFixtureSources
{
    /// <summary>Stands in for a NuGet package: its assembly is never given to the graph.</summary>
    public const string Package = """
        namespace Fixture.Package
        {
            public interface IPackageHandler
            {
                void Handle();
            }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ExportAttribute : System.Attribute
            {
            }

            public sealed class PackageType
            {
            }
        }
        """;

    /// <summary>The NUnit attributes the graph recognizes by name; compiled as <c>nunit.framework</c>.</summary>
    public const string NUnitFramework = """
        namespace NUnit.Framework
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class TestAttribute : System.Attribute
            {
            }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class SetUpFixtureAttribute : System.Attribute
            {
            }
        }
        """;

    /// <summary>The MSTest attributes the graph recognizes by name.</summary>
    public const string MSTestFramework = """
        namespace Microsoft.VisualStudio.TestTools.UnitTesting
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class TestClassAttribute : System.Attribute
            {
            }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class TestMethodAttribute : System.Attribute
            {
            }
        }
        """;

    /// <summary>Production code: services behind a container and one pair of types per edge kind.</summary>
    public const string ShopLib = """
        using System;
        using System.Reflection;
        using System.Threading.Tasks;

        namespace Shop
        {
            public interface IOrderService
            {
                int Count();
            }

            public sealed class OrderService : IOrderService
            {
                public int Count() => 1;
            }

            // Names the contract only, as code that gets its dependency from a container does.
            public sealed class OrderConsumer
            {
                private readonly IOrderService _service;

                public OrderConsumer(IOrderService service)
                {
                    _service = service;
                }

                public int Total() => _service.Count();
            }

            // Stands in for a container: explicit registration and registration by scanning an assembly.
            public static class Registry
            {
                public static void Add<TService, TImplementation>()
                    where TImplementation : TService
                {
                }

                public static void Scan(Assembly assembly)
                {
                }

                public static void FromAssemblyOf<T>()
                {
                }
            }

            public static class OrderRegistration
            {
                public static void Register() => Registry.Add<IOrderService, OrderService>();
            }

            public static class AssemblyScanner
            {
                public static void Register() => Registry.Scan(typeof(OrderConsumer).Assembly);
            }

            public static class GenericScanner
            {
                public static void Register() => Registry.FromAssemblyOf<OrderConsumer>();
            }

            // In a test run the entry assembly is the test host: nothing is loaded by a name.
            public static class EntryAssemblyReader
            {
                public static string Name() => Assembly.GetEntryAssembly()?.GetName().Name;
            }

            public interface IUnrelated
            {
            }

            // Implements a contract that no test reaches and carries no mark for a framework.
            public sealed class UnrelatedService : IUnrelated
            {
            }

            // Implements an interface of a package, as a controller or a hosted service does.
            public sealed class FrameworkFoundHandler : Fixture.Package.IPackageHandler
            {
                public void Handle()
                {
                }
            }

            [Fixture.Package.Export]
            public sealed class ExportedPart
            {
            }

            public sealed class PolicyScanner
            {
                public static void Register() => Registry.Scan(typeof(PolicyScanner).Assembly);
            }

            // The contract names the scanner in a signature, so a walk gets to the scanner only through the
            // contract: the assembly is scanned after the contract is done, whatever numbers the types get.
            public interface IPricingPolicy
            {
                PolicyScanner Scanner { get; }
            }

            public sealed class PricingPolicy : IPricingPolicy
            {
                public PolicyScanner Scanner => null;
            }

            public sealed class SetupTarget
            {
            }

            public sealed class ModuleInitializerTarget
            {
            }
        }

        namespace Shop.Edges
        {
            public class BaseTarget
            {
            }

            public interface IInterfaceTarget
            {
            }

            public sealed class SupertypeUser : BaseTarget, IInterfaceTarget
            {
            }

            public sealed class FieldTarget
            {
            }

            public sealed class ParameterTarget
            {
            }

            public sealed class ReturnTarget
            {
            }

            public abstract class SignatureUser
            {
                public FieldTarget Field;

                public abstract ReturnTarget Convert(ParameterTarget value);
            }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class MarkerAttribute : Attribute
            {
                public MarkerAttribute(Type type)
                {
                }
            }

            public sealed class AttributeArgumentTarget
            {
            }

            [Marker(typeof(AttributeArgumentTarget))]
            public sealed class AttributeUser
            {
            }

            public sealed class LocalTarget
            {
            }

            public static class LocalUser
            {
                public static void Run()
                {
                    LocalTarget local = null;
                    GC.KeepAlive(local);
                }
            }

            public sealed class TypeTokenTarget
            {
            }

            public static class TypeTokenUser
            {
                public static Type Run() => typeof(TypeTokenTarget);
            }

            public static class MethodTokenTarget
            {
                public static int Value() => 1;
            }

            public static class MethodTokenUser
            {
                public static int Run() => MethodTokenTarget.Value();
            }

            public static class FieldTokenTarget
            {
                public static int Value;
            }

            public static class FieldTokenUser
            {
                public static int Run() => FieldTokenTarget.Value;
            }

            public static class LambdaTarget
            {
                public static int Value() => 1;
            }

            public static class AsyncTarget
            {
                public static int Value() => 2;
            }

            public static class LambdaUser
            {
                public static Func<int> Deferred() => () => LambdaTarget.Value();

                public static async Task<int> LaterAsync()
                {
                    await Task.Yield();
                    return AsyncTarget.Value();
                }
            }

            public static class SwitchTarget
            {
                public static int Value() => 50;
            }

            // A dense switch compiles to the IL switch instruction, whose operand has a variable length.
            public static class SwitchUser
            {
                public static int Pick(int value)
                {
                    switch (value)
                    {
                        case 0:
                            return 10;
                        case 1:
                            return 20;
                        case 2:
                            return 30;
                        case 3:
                            return 40;
                        default:
                            return SwitchTarget.Value();
                    }
                }
            }

            public sealed class TypeSwitchTarget
            {
            }

            public static class TypeSwitchUser
            {
                public static int Pick(object value)
                {
                    switch (value)
                    {
                        case TypeSwitchTarget:
                            return 1;
                        case string:
                            return 2;
                        default:
                            return 0;
                    }
                }
            }

            public static class ConstantHolder
            {
                public const int Limit = 42;
            }

            public static class ConstantUser
            {
                public static int Limit() => ConstantHolder.Limit;
            }

            public sealed class NameofTarget
            {
            }

            public static class NameofUser
            {
                public static string Name() => nameof(NameofTarget);
            }

            public class ConstraintTarget
            {
            }

            public sealed class ConstraintUser<T>
                where T : ConstraintTarget
            {
            }

            public sealed class CatchTarget : Exception
            {
            }

            public static class CatchUser
            {
                public static void Run(Action action)
                {
                    try
                    {
                        action();
                    }
                    catch (CatchTarget)
                    {
                    }
                }
            }

            public static class NestedTarget
            {
                public static void Touch()
                {
                }
            }

            public sealed class NestedOwner
            {
                private sealed class Inner
                {
                    public void Run() => NestedTarget.Touch();
                }
            }
        }
        """;

    /// <summary>An assembly that no code references: only a load by name brings it in.</summary>
    public const string ShopPlugins = """
        namespace Shop.Plugins
        {
            public sealed class ReportPlugin
            {
            }
        }
        """;

    /// <summary>A third project, whose output directory holds a copy of the test assembly.</summary>
    public const string ShopOther = """
        namespace Shop.Other
        {
            public sealed class OtherThing
            {
            }
        }
        """;

    public const string ShopTests = """
        using System;
        using System.Reflection;
        using System.Runtime.CompilerServices;
        using System.Runtime.Loader;
        using Xunit;

        namespace Shop.Tests
        {
            // A unit test: it names the consumer and never hands an assembly to reflection.
            public sealed class ReferenceOnlyTests
            {
                [Fact]
                public void NamesConsumer() => GC.KeepAlive(typeof(OrderConsumer));
            }

            public sealed class RegistrationTests
            {
                [Fact]
                public void NamesRegistration() => GC.KeepAlive(typeof(OrderRegistration));
            }

            // Builds its services by scanning an assembly.
            public sealed class ContainerTests
            {
                [Fact]
                public void NamesConsumerAndScanner()
                {
                    GC.KeepAlive(typeof(OrderConsumer));
                    GC.KeepAlive(typeof(AssemblyScanner));
                }
            }

            public sealed class GenericContainerTests
            {
                [Fact]
                public void NamesConsumerAndScanner()
                {
                    GC.KeepAlive(typeof(OrderConsumer));
                    GC.KeepAlive(typeof(GenericScanner));
                }
            }

            // Hands the assembly of a library type to reflection from the test assembly itself: the scanned
            // assembly is the one of the named type, not the one of the calling code.
            public sealed class CrossAssemblyScanTests
            {
                [Fact]
                public void ScansTheLibrary() => Registry.Scan(typeof(OrderConsumer).Assembly);
            }

            public sealed class CrossAssemblyGenericScanTests
            {
                [Fact]
                public void ScansTheLibrary() => Registry.FromAssemblyOf<OrderConsumer>();
            }

            // Names a contract whose own signature leads to the code that scans the assembly.
            public sealed class ContractFirstTests
            {
                [Fact]
                public void NamesContract() => GC.KeepAlive(typeof(IPricingPolicy));
            }

            // Names the scanner only. The contract IOrderService is two steps behind it (through
            // OrderConsumer), so the assembly is scanned before the contract is reached.
            public sealed class ScannerFirstTests
            {
                [Fact]
                public void NamesScanner() => GC.KeepAlive(typeof(AssemblyScanner));
            }

            public sealed class EntryAssemblyTests
            {
                [Fact]
                public void NamesConsumerAndReader()
                {
                    GC.KeepAlive(typeof(OrderConsumer));
                    GC.KeepAlive(typeof(EntryAssemblyReader));
                }
            }

            // Loads an assembly by a name that only a string holds, the way a plugin host does.
            public static class ModuleLoader
            {
                public static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));
            }

            public sealed class ModuleLoaderTests
            {
                [Fact]
                public void NamesLoader() => GC.KeepAlive(typeof(ModuleLoader));
            }

            public static class ContextLoader
            {
                public static Assembly Load(string path) => AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }

            public sealed class ContextLoaderTests
            {
                [Fact]
                public void NamesLoader() => GC.KeepAlive(typeof(ContextLoader));
            }

            // Enumerates the assemblies the process has loaded, the way a type finder does.
            public static class LoadedAssemblyScanner
            {
                public static int Count() => AppDomain.CurrentDomain.GetAssemblies().Length;
            }

            public sealed class LoadedAssemblyTests
            {
                [Fact]
                public void NamesScanner() => GC.KeepAlive(typeof(LoadedAssemblyScanner));
            }

            public abstract class AbstractFixtureTests
            {
                [Fact]
                public void Inherited()
                {
                }
            }

            public sealed class DerivedFixtureTests : AbstractFixtureTests
            {
                [Theory]
                [InlineData(1)]
                public void Own(int value)
                {
                }
            }

            public sealed class NUnitStyleTests
            {
                [NUnit.Framework.Test]
                public void Runs()
                {
                }
            }

            [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
            public sealed class MSTestStyleTests
            {
                [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
                public void Runs()
                {
                }
            }

            // Derived from a test attribute under a name that says nothing about tests.
            public sealed class SlowAttribute : FactAttribute
            {
            }

            public sealed class CustomAttributeTests
            {
                [Slow]
                public void Runs()
                {
                }
            }

            public sealed class NotATest
            {
                public void Runs()
                {
                }
            }

            // A helper of the test assembly that a framework finds by itself.
            public sealed class TestSideHandler : Fixture.Package.IPackageHandler
            {
                public void Handle()
                {
                }
            }

            public static class TestAssemblyScanner
            {
                public static void Register() => Registry.Scan(typeof(TestAssemblyScanner).Assembly);
            }

            public sealed class TestAssemblyScanTests
            {
                [Fact]
                public void NamesScanner() => GC.KeepAlive(typeof(TestAssemblyScanner));
            }

            [NUnit.Framework.SetUpFixture]
            public sealed class GlobalSetup
            {
                public void Run() => GC.KeepAlive(typeof(SetupTarget));
            }

            internal static class ModuleSetup
            {
                [ModuleInitializer]
                internal static void Initialize() => GC.KeepAlive(typeof(ModuleInitializerTarget));
            }
        }
        """;

    /// <summary>The library of the neighbor-map cases. The test assembly below does not reference it.</summary>
    public const string NeighborLib = """
        namespace Lib
        {
            public sealed class LibType
            {
            }
        }
        """;

    public const string NeighborTests = """
        namespace Tests
        {
            public sealed class LoaderTests
            {
                [Xunit.Fact]
                public void Loads() => System.Reflection.Assembly.Load("Lib");
            }

            public sealed class PlainTests
            {
                [Xunit.Fact]
                public void Runs()
                {
                }
            }
        }
        """;

    public const string NeighborOther = """
        namespace Other
        {
            public sealed class OtherType
            {
            }
        }
        """;

    /// <summary>An assembly that is not a project of the solution: it is never among the inputs.</summary>
    public const string NeighborPackage = """
        namespace Package
        {
            public sealed class PackageType
            {
            }
        }
        """;

    /// <summary>One project built for two target frameworks: the same assembly name with different code.</summary>
    public const string MultiFirstFramework = """
        namespace Multi
        {
            public sealed class FirstTarget
            {
            }

            public sealed class SecondTarget
            {
            }

            public static class User
            {
                public static object Run() => new FirstTarget();
            }
        }
        """;

    public const string MultiSecondFramework = """
        namespace Multi
        {
            public sealed class FirstTarget
            {
            }

            public sealed class SecondTarget
            {
            }

            public sealed class OnlyInSecond
            {
            }

            public static class User
            {
                public static object Run() => new SecondTarget();
            }
        }
        """;
}
