using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class VirtualReferenceClassifierTests
{
    [Fact]
    public void ValidateDirectOnlyRequiresFilePath_true_without_file_returns_error()
    {
        var error = VirtualReferenceClassifier.ValidateDirectOnlyRequiresFilePath(directOnly: true, hasFilePath: false);
        Assert.Equal(VirtualReferenceClassifier.DirectOnlyRequiresFilePathError, error);
        Assert.Contains("filePath", error, StringComparison.Ordinal);
        Assert.Contains("declaring type", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateDirectOnlyRequiresFilePath_false_or_with_file_is_null()
    {
        Assert.Null(VirtualReferenceClassifier.ValidateDirectOnlyRequiresFilePath(directOnly: false, hasFilePath: false));
        Assert.Null(VirtualReferenceClassifier.ValidateDirectOnlyRequiresFilePath(directOnly: true, hasFilePath: true));
        Assert.Null(VirtualReferenceClassifier.ValidateDirectOnlyRequiresFilePath(directOnly: false, hasFilePath: true));
    }

    [Fact]
    public void TypesMatch_metadata_derived_receiver_matches_source_declaring_type()
    {
        const string libSource = """
            namespace Lib;
            public class Base
            {
                public virtual void Run() { }
            }
            public class Derived : Base
            {
            }
            """;

        var libCompilation = CreateCompilation(libSource, "Lib");
        using var peStream = new MemoryStream();
        var emit = libCompilation.Emit(peStream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        peStream.Position = 0;
        var metadata = MetadataReference.CreateFromStream(peStream);

        var consumerCompilation = CreateCompilation(
            """
            namespace Consumer;
            public class Holder
            {
                public void Call(Lib.Derived d) { d.Run(); }
            }
            """,
            "Consumer",
            metadata);

        var sourceLib = CreateCompilation(libSource, "LibSource");
        var sourceBase = sourceLib.GetTypeByMetadataName("Lib.Base")!;
        var metadataDerived = consumerCompilation.GetTypeByMetadataName("Lib.Derived")!;

        Assert.False(SymbolEqualityComparer.Default.Equals(sourceBase, metadataDerived));
        Assert.True(VirtualReferenceClassifier.IsSameOrDerivedFrom(metadataDerived, sourceBase));
        Assert.True(VirtualReferenceClassifier.TypesMatch(
            metadataDerived.BaseType!,
            sourceBase));
    }

    [Fact]
    public void TypesMatch_base_receiver_does_not_match_override_declaring_type()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;
            public class Base
            {
                public virtual void Run() { }
            }
            public class Derived : Base
            {
                public override void Run() { }
            }
            """);

        var baseType = compilation.GetTypeByMetadataName("Demo.Base")!;
        var derivedType = compilation.GetTypeByMetadataName("Demo.Derived")!;

        Assert.False(VirtualReferenceClassifier.IsSameOrDerivedFrom(baseType, derivedType));
        Assert.True(VirtualReferenceClassifier.IsSameOrDerivedFrom(derivedType, derivedType));
        Assert.True(VirtualReferenceClassifier.IsSameOrDerivedFrom(derivedType, baseType));
    }

    [Fact]
    public void TypesMatch_grandchild_matches_override_declaring_type()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;
            public class Base
            {
                public virtual void Run() { }
            }
            public class Derived : Base
            {
                public override void Run() { }
            }
            public class Grandchild : Derived
            {
            }
            """);

        var derived = compilation.GetTypeByMetadataName("Demo.Derived")!;
        var grandchild = compilation.GetTypeByMetadataName("Demo.Grandchild")!;

        Assert.True(VirtualReferenceClassifier.IsSameOrDerivedFrom(grandchild, derived));
    }

    [Fact]
    public void TypesMatch_open_generic_and_closed_generic_match()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;
            public class Box<T>
            {
                public virtual void Run() { }
            }
            public class Uses
            {
                public void Call(Box<int> box) { box.Run(); }
            }
            """);

        var open = compilation.GetTypeByMetadataName("Demo.Box`1")!;
        Assert.True(open.IsGenericType);
        Assert.True(SymbolEqualityComparer.Default.Equals(open, open.ConstructedFrom));

        var uses = compilation.GetTypeByMetadataName("Demo.Uses")!;
        var call = uses.GetMembers("Call").OfType<IMethodSymbol>().Single();
        var closed = call.Parameters[0].Type as INamedTypeSymbol;
        Assert.NotNull(closed);
        Assert.False(SymbolEqualityComparer.Default.Equals(open, closed));
        Assert.True(VirtualReferenceClassifier.TypesMatch(open, closed!));
        Assert.True(VirtualReferenceClassifier.TypesMatch(closed!, open));
        Assert.True(VirtualReferenceClassifier.IsSameOrDerivedFrom(closed, open));
    }

    [Fact]
    public void IsApplicableMethod_skips_interface_and_ordinary_methods()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;
            public interface IThing
            {
                void Run();
            }
            public class C
            {
                public void Ordinary() { }
                public virtual void Virtual() { }
            }
            """);

        var ifaceMethod = compilation.GetTypeByMetadataName("Demo.IThing")!.GetMembers("Run").Single();
        var ordinary = compilation.GetTypeByMetadataName("Demo.C")!.GetMembers("Ordinary").Single();
        var virt = compilation.GetTypeByMetadataName("Demo.C")!.GetMembers("Virtual").Single();

        Assert.False(VirtualReferenceClassifier.IsApplicableMethod(ifaceMethod));
        Assert.False(VirtualReferenceClassifier.IsApplicableMethod(ordinary));
        Assert.True(VirtualReferenceClassifier.IsApplicableMethod(virt));
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "VirtualRef.Tests",
        params MetadataReference[] extraReferences)
    {
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
        };
        references.AddRange(extraReferences);

        var tree = CSharpSyntaxTree.ParseText(SourceText.From(source), path: assemblyName + ".cs");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        return compilation;
    }
}
