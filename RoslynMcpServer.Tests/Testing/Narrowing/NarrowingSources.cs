namespace RoslynMcpServer.Tests.Testing.Narrowing;

/// <summary>
/// Sources of the narrowing cases. A case takes <see cref="Lib"/> before the edit and a variant made by one
/// replacement after it, and a test project made of the test classes it needs.
/// </summary>
internal static class NarrowingSources
{
    /// <summary>An attribute of a package: a type that carries it is found by a framework, not named by code.</summary>
    internal const string Package = """
        namespace Package
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ExportAttribute : System.Attribute
            {
            }
        }
        """;

    internal const string Lib = """
        using System.Reflection;

        namespace Lib
        {
            public interface IWorker
            {
            }

            public class A : IWorker
            {
                public int Run() => 1;
            }

            public class B
            {
                public int Other() => 2;
            }

            public class Untested
            {
                public int Value() => 3;
            }

            public interface IService
            {
            }

            public sealed class Handler : IService
            {
            }

            public sealed class OldHandler : IService { }

            [Package.Export]
            public sealed class Part
            {
            }

            // Keeps the reference to the package when Part loses its attribute: a dropped assembly reference
            // changes the fingerprint of the assembly, and every type of it would count as changed.
            public sealed class PackageUser
            {
                public Package.ExportAttribute Attribute { get; } = new();
            }

            public static class Registry
            {
                public static void Scan(Assembly assembly)
                {
                }
            }

            public static class Loader
            {
                public static Assembly Load(string name) => Assembly.Load(name);
            }

            public static class Limits
            {
                public const int Max = 1;
            }
        }
        """;

    /// <summary>A middle project that only passes a library type on.</summary>
    internal const string Mid = """
        namespace Mid
        {
            public static class Relay
            {
                public static int Run() => new Lib.A().Run();
            }
        }
        """;

    /// <summary>A library that does not depend on <see cref="Lib"/>.</summary>
    internal const string Helper = """
        namespace Helper
        {
            public static class Tools
            {
                public static int Value() => 4;
            }
        }
        """;

    /// <summary>A library off the edit path that loads assemblies by name.</summary>
    internal const string HelperWithLoader = """
        namespace Helper
        {
            public static class HLoader
            {
                public static System.Reflection.Assembly Load(string name) => System.Reflection.Assembly.Load(name);
            }
        }
        """;

    /// <summary>A project no other project references; a build may still copy it next to a test assembly.</summary>
    internal const string Plugin = """
        namespace Plugin
        {
            public static class Tool
            {
                public static int Value() => 5;
            }
        }
        """;

    internal const string ReferencesA = """
            public sealed class ReferencesA
            {
                [Fact]
                public void Runs() => GC.KeepAlive(new Lib.A().Run());
            }
        """;

    /// <summary>Gets <c>A</c> only by scanning the library for implementations of a contract it names.</summary>
    internal const string ScansWorkers = """
            public sealed class ScansWorkers
            {
                [Fact]
                public void Finds() => Lib.Registry.Scan(typeof(Lib.IWorker).Assembly);
            }
        """;

    /// <summary>Reaches code that loads an assembly by a name only a string holds.</summary>
    internal const string LoadsByName = """
            public sealed class LoadsByName
            {
                [Fact]
                public void Loads() => GC.KeepAlive(typeof(Lib.Loader));
            }
        """;

    internal const string ReferencesB = """
            public sealed class ReferencesB
            {
                [Fact]
                public void Other() => GC.KeepAlive(new Lib.B().Other());
            }
        """;

    /// <summary>Counts implementations of <c>IService</c> by scanning; never names <c>Handler</c>.</summary>
    internal const string CountsServices = """
            public sealed class CountsServices
            {
                [Fact]
                public void Counts() => Lib.Registry.Scan(typeof(Lib.IService).Assembly);
            }
        """;

    /// <summary>Scans the library; finds what a framework finds there, never names <c>Part</c>.</summary>
    internal const string ScansParts = """
            public sealed class ScansParts
            {
                [Fact]
                public void Finds() => Lib.Registry.Scan(typeof(Lib.Registry).Assembly);
            }
        """;

    internal const string UsesLimit = """
            public sealed class UsesLimit
            {
                [Fact]
                public void Max() => GC.KeepAlive(Lib.Limits.Max);
            }
        """;

    /// <summary>A test class that the edit deletes together with the type it used.</summary>
    internal const string DeletedTests = """
            public sealed class DeletedTests
            {
                [Fact]
                public void Runs() => GC.KeepAlive(new Lib.A());
            }
        """;

    internal const string UsesRelay = """
            public sealed class UsesRelay
            {
                [Fact]
                public void Runs() => GC.KeepAlive(Mid.Relay.Run());
            }
        """;

    internal const string UsesHelperLoader = """
            public sealed class UsesHelperLoader
            {
                [Fact]
                public void Loads() => GC.KeepAlive(typeof(Helper.HLoader));
            }
        """;

    /// <summary>A test class the edit adds.</summary>
    internal const string AddedTests = """
            public sealed class AddedTests
            {
                [Fact]
                public void Other() => GC.KeepAlive(new Lib.B());
            }
        """;

    internal const string UsesHelper = """
            public sealed class UsesHelper
            {
                [Fact]
                public void Runs() => GC.KeepAlive(Helper.Tools.Value());
            }
        """;

    internal static string TestClasses(params string[] classes) => TestClassesIn("Tests", classes);

    internal static string TestClassesIn(string ns, params string[] classes) => $$"""
        using System;
        using Xunit;

        namespace {{ns}}
        {
        {{string.Join(Environment.NewLine, classes)}}
        }
        """;

    /// <summary><see cref="Lib"/> with one replacement that must apply exactly once.</summary>
    internal static string LibWith(string oldText, string newText)
    {
        var index = Lib.IndexOf(oldText, StringComparison.Ordinal);
        if (index < 0 || Lib.IndexOf(oldText, index + 1, StringComparison.Ordinal) >= 0)
        {
            throw new ArgumentException("The replaced text must occur in the library source exactly once: " + oldText);
        }

        return Lib.Replace(oldText, newText, StringComparison.Ordinal);
    }
}
