using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using static RoslynMcpServer.Services.Testing.Il.IlFingerprintEncoding;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>Reads the exact storage extent of an RVA field, without section padding or neighboring data.</summary>
internal sealed class IlFingerprintFieldData
{
    public IlFingerprintFieldData(PEReader pe, MetadataReader metadata, IlFingerprintNames names)
    {
        _pe = pe;
        _metadata = metadata;
        _names = names;
        var flags = pe.PEHeaders.CorHeader!.Flags;
        _pointerSize = pe.PEHeaders.PEHeader!.Magic == PEMagic.PE32Plus ? 8
            : (flags & CorFlags.Requires32Bit) != 0 || (flags & CorFlags.ILOnly) == 0 ? 4 : 0;
    }

    public string Read(FieldDefinition field, string signature)
    {
        var rva = field.GetRelativeVirtualAddress();
        if (rva == 0)
        {
            return "no-data";
        }

        var size = Layout(signature).Size;
        var content = _pe.GetSectionData(rva).GetContent(0, size);
        return Value("data", Convert.ToHexString(content.AsSpan()));
    }

    private (int Size, int Alignment) Layout(string signature)
    {
        signature = _names.StorageType(signature);
        if (_layouts.TryGetValue(signature, out var known))
        {
            return known;
        }

        if (_names.IsPointer(signature))
        {
            return PointerLayout();
        }

        if (_names.TryPrimitive(signature, out var primitive))
        {
            if (primitive is PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr)
            {
                return PointerLayout();
            }

            var size = IlFingerprintNames.PrimitiveWidth(primitive, _pointerSize);
            return (size, size);
        }

        if (!_active.Add(signature))
        {
            throw new BadImageFormatException("Cyclic field storage layout.");
        }

        var layout = DefinitionLayout(_names.Definition(signature));
        _active.Remove(signature);
        _layouts.Add(signature, layout);
        return layout;
    }

    private (int Size, int Alignment) DefinitionLayout(TypeDefinitionHandle handle)
    {
        var definition = _metadata.GetTypeDefinition(handle);
        var declared = definition.GetLayout();
        var pack = declared.PackingSize == 0 ? 8 : declared.PackingSize;
        var explicitLayout = (definition.Attributes & TypeAttributes.LayoutMask) == TypeAttributes.ExplicitLayout;
        var size = 0;
        var alignment = 1;
        foreach (var fieldHandle in definition.GetFields())
        {
            var field = _metadata.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.Static) != 0)
            {
                continue;
            }

            var storage = Layout(field.DecodeSignature(_names, null));
            var fieldAlignment = Math.Min(pack, storage.Alignment);
            var offset = explicitLayout ? field.GetOffset() : RoundUp(size, fieldAlignment);
            if (offset < 0)
            {
                throw new BadImageFormatException("An explicit-layout field has no offset.");
            }

            size = Math.Max(size, checked(offset + storage.Size));
            alignment = Math.Max(alignment, fieldAlignment);
        }

        return (RoundUp(Math.Max(Math.Max(size, declared.Size), 1), alignment), alignment);
    }

    private (int Size, int Alignment) PointerLayout()
    {
        // AnyCPU PE32 can run as 64-bit: hashing four bytes would miss changes in the upper native word.
        // See docs/rapid-tia-2/mvp/evidence/task-02-review-round-1.md, F-01.
        if (_pointerSize == 0)
        {
            throw new BadImageFormatException("The RVA storage size is runtime-dependent in this PE32 image.");
        }

        return (_pointerSize, _pointerSize);
    }

    private static int RoundUp(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private readonly PEReader _pe;
    private readonly MetadataReader _metadata;
    private readonly IlFingerprintNames _names;
    private readonly int _pointerSize;
    private readonly Dictionary<string, (int Size, int Alignment)> _layouts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
}
