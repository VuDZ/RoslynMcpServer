using System.Text;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class SourceTextEncodingTests : IDisposable
{
    private static readonly UTF8Encoding BomFreeUtf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "RoslynMcpSourceTextEncoding-" + Guid.NewGuid().ToString("N"));

    public SourceTextEncodingTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ResolveForWrite_prefers_existing_document_encoding()
    {
        var existing = SourceText.From("// legacy", Encoding.Unicode);

        var resolved = SourceTextEncoding.ResolveForWrite(existing, SourceText.From("// candidate"), diskPath: null);

        Assert.Equal(Encoding.Unicode.CodePage, resolved.CodePage);
    }

    [Fact]
    public void ResolveForWrite_uses_candidate_encoding_when_existing_is_unknown()
    {
        var resolved = SourceTextEncoding.ResolveForWrite(
            existing: null,
            candidate: SourceText.From("// added", Utf8WithBom),
            diskPath: null);

        Assert.NotEmpty(resolved.GetPreamble());
    }

    [Fact]
    public void ResolveForWrite_falls_back_to_disk_bom_state()
    {
        var path = Path.Combine(_root, "WithBom.cs");
        File.WriteAllText(path, "class A { }", Utf8WithBom);

        var resolved = SourceTextEncoding.ResolveForWrite(existing: null, candidate: null, diskPath: path);

        Assert.Equal(Utf8WithBom.GetPreamble(), resolved.GetPreamble());
    }

    [Fact]
    public void ForDiskPath_returns_bom_free_utf8_for_absent_file()
    {
        var resolved = SourceTextEncoding.ForDiskPath(Path.Combine(_root, "Missing.cs"));

        Assert.Equal(BomFreeUtf8.GetPreamble(), resolved.GetPreamble());
    }

    [Fact]
    public void ForDiskPath_returns_bom_free_utf8_for_bom_free_file()
    {
        var path = Path.Combine(_root, "NoBom.cs");
        File.WriteAllText(path, "class A { }", BomFreeUtf8);

        var resolved = SourceTextEncoding.ForDiskPath(path);

        Assert.Equal(BomFreeUtf8.GetPreamble(), resolved.GetPreamble());
    }

    [Fact]
    public void ForDiskPath_preserves_utf8_bom()
    {
        var path = Path.Combine(_root, "WithBom.cs");
        File.WriteAllText(path, "class A { }", Utf8WithBom);

        var resolved = SourceTextEncoding.ForDiskPath(path);

        Assert.Equal(Utf8WithBom.GetPreamble(), resolved.GetPreamble());
    }

    [Fact]
    public void ForDiskPath_preserves_utf16_bom()
    {
        var path = Path.Combine(_root, "Utf16.cs");
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        File.WriteAllText(path, "class A { }", utf16);

        var resolved = SourceTextEncoding.ForDiskPath(path);

        Assert.Equal(utf16.GetPreamble(), resolved.GetPreamble());
    }
}
