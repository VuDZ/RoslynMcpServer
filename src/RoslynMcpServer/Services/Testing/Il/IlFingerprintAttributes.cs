using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using static RoslynMcpServer.Services.Testing.Il.IlFingerprintEncoding;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>Normalizes only typed System.Type values, preserving ordinary strings and every other value byte.</summary>
internal sealed class IlFingerprintAttributes
{
    public IlFingerprintAttributes(MetadataReader metadata, IlFingerprintNames names)
    {
        _metadata = metadata;
        _names = names;
    }

    public string Read(IEnumerable<CustomAttributeHandle> handles, bool excludeBuildVersions = false) =>
        Set("attributes", handles.Select(_metadata.GetCustomAttribute)
            .Where(attribute => !excludeBuildVersions || !_names.IsBuildVersionConstructor(attribute.Constructor))
            .Select(Read));

    public string ReadSecurity(BlobHandle handle, IReadOnlySet<string> solutionAssemblies)
    {
        if (handle.IsNil)
        {
            return "nil";
        }

        var bytes = _metadata.GetBlobBytes(handle);
        if (bytes.Length == 0 || bytes[0] != 0x2E)
        {
            return Convert.ToHexString(bytes);
        }

        // Binary permissions embed qualified attribute types and CA-style typed values, rather than opaque text.
        // See docs/rapid-tia-2/mvp/evidence/task-02-review-round-3.md, R3-F-02.
        var position = 1;
        var count = ReadCompressedInteger(bytes, ref position);
        var permissions = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var type = ReadString(bytes, ref position)
                ?? throw new BadImageFormatException("A security attribute has no type name.");
            var length = ReadCompressedInteger(bytes, ref position);
            if (length > bytes.Length - position)
            {
                throw new BadImageFormatException("Truncated security attribute arguments.");
            }

            var arguments = bytes.AsSpan(position, length).ToArray();
            position += length;
            var argumentPosition = 0;
            var namedCount = ReadCompressedInteger(arguments, ref argumentPosition);
            var state = new ParseState(argumentPosition, "", ImmutableDictionary<string, int>.Empty, solutionAssemblies);
            var values = NamedArguments(state, arguments, namedCount).Distinct(StringComparer.Ordinal).ToArray();
            if (values.Length == 0)
            {
                throw new BadImageFormatException("The complete security attribute arguments could not be decoded.");
            }

            permissions.Add(Value("permission", _names.SerializedType(type, solutionAssemblies), Set("interpretations", values)));
        }

        if (position != bytes.Length)
        {
            throw new BadImageFormatException("Unexpected data after the binary permission set.");
        }

        return Sequence("permissions", permissions);
    }

    private string Read(CustomAttribute attribute)
    {
        var bytes = _metadata.GetBlobBytes(attribute.Value);
        if (bytes.Length < 4 || bytes[0] != 1 || bytes[1] != 0)
        {
            throw new BadImageFormatException("Invalid custom attribute prolog.");
        }

        var states = new List<ParseState> {new(2, "", ImmutableDictionary<string, int>.Empty)};
        foreach (var type in _names.MethodSignature(attribute.Constructor).ParameterTypes)
        {
            states = Advance(states, type, bytes);
        }

        var results = new List<string>();
        foreach (var state in states)
        {
            try
            {
                results.AddRange(NamedArguments(state, bytes));
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
            {
                // An unknown external enum width is accepted only if the entire attribute parses.
            }
        }

        if (results.Count == 0)
        {
            throw new BadImageFormatException("The complete custom attribute value could not be decoded.");
        }

        // External enum definitions are absent from the input image. Retain every complete interpretation,
        // with one consistent width per enum identity; choosing the first could discard behavior bytes.
        return Value("attribute", _names.Resolve(attribute.Constructor), Set("interpretations", results.Distinct(StringComparer.Ordinal)));
    }

    private IEnumerable<string> NamedArguments(ParseState state, byte[] bytes, int? namedCount = null)
    {
        var position = state.Position;
        var count = namedCount ?? ReadInteger(bytes, ref position, 2);
        if (count < 0 || count > (bytes.Length - position) / 3)
        {
            throw new BadImageFormatException("Invalid named argument count.");
        }
        var states = new List<ParseState> {state with {Position = position}};
        for (var i = 0; i < count; i++)
        {
            var next = new List<ParseState>();
            foreach (var candidate in states)
            {
                try
                {
                    position = candidate.Position;
                    var kind = ReadInteger(bytes, ref position, 1);
                    if (kind is not 0x53 and not 0x54)
                    {
                        continue;
                    }

                    var type = FieldType(bytes, ref position, candidate.SolutionAssemblies);
                    var name = ReadString(bytes, ref position);
                    next.AddRange(ReadValue(type, candidate with
                    {
                        Position = position,
                        // Empty and null arrays have no element values from which to recover their declared type.
                        Value = candidate.Value + Value("named", kind, type, name),
                    }, bytes));
                }
                catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
                {
                    // Reject this width assignment, keeping other complete interpretations.
                }
            }

            states = next;
            CheckCount(states.Count);
        }

        return states.Where(candidate => candidate.Position == bytes.Length).Select(candidate => candidate.Value);
    }

    private List<ParseState> Advance(List<ParseState> states, string type, byte[] bytes)
    {
        var next = new List<ParseState>();
        foreach (var state in states)
        {
            try
            {
                next.AddRange(ReadValue(type, state, bytes));
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
            {
                // A wrong external enum width usually fails at the next argument.
            }
        }

        CheckCount(next.Count);
        return next;
    }

    private List<ParseState> ReadValue(string type, ParseState state, byte[] bytes)
    {
        var position = state.Position;
        if (type == NullObject)
        {
            return [Append(state, position, "null-object")];
        }

        if (type == SystemType || _names.IsSystemType(type))
        {
            var name = ReadString(bytes, ref position);
            return [Append(state, position, Value("typeof", name is null ? "null" : SerializedType(name, state.SolutionAssemblies)))];
        }

        if (_names.TryArrayElement(type, out var element))
        {
            var count = ReadInteger(bytes, ref position, 4);
            if (count < -1 || count > bytes.Length - position)
            {
                throw new BadImageFormatException("Invalid attribute array length.");
            }

            var states = new List<ParseState> {Append(state, position, Value("array", count))};
            for (var i = 0; i < count; i++)
            {
                states = Advance(states, element, bytes);
            }

            return states;
        }

        if (_names.TryPrimitive(type, out var primitive))
        {
            if (primitive == PrimitiveTypeCode.String)
            {
                var text = ReadString(bytes, ref position, preserveSurrogates: true);
                return [Append(state, position, Value("string", text is null, text))];
            }

            if (primitive == PrimitiveTypeCode.Object)
            {
                var actualType = FieldType(bytes, ref position, state.SolutionAssemblies);
                return ReadValue(actualType, Append(state, position, Value("boxed", actualType)), bytes);
            }

            var width = IlFingerprintNames.PrimitiveWidth(primitive, pointerSize: 0);
            return [ReadBytes(state, type, width, bytes)];
        }

        var identity = _names.AttributeIdentity(type);
        var knownWidth = _names.EnumWidth(type);
        if (state.EnumWidths.TryGetValue(identity, out var assigned))
        {
            return [ReadBytes(state, identity, assigned, bytes)];
        }

        var result = new List<ParseState>();
        foreach (var width in knownWidth.HasValue ? new[] {knownWidth.Value} : new[] {1, 2, 4, 8})
        {
            if (bytes.Length - position >= width)
            {
                result.Add(ReadBytes(state with {EnumWidths = state.EnumWidths.Add(identity, width)}, identity, width, bytes));
            }
        }

        return result;
    }

    private string FieldType(byte[] bytes, ref int position, IReadOnlySet<string>? solutionAssemblies = null)
    {
        var code = ReadInteger(bytes, ref position, 1);
        return code switch
        {
            0x50 => SystemType,
            0x51 => _names.GetPrimitiveType(PrimitiveTypeCode.Object),
            0xFF => NullObject,
            0x55 => SerializedType(ReadString(bytes, ref position)
                ?? throw new BadImageFormatException("An attribute enum has no type name."), solutionAssemblies),
            0x1D => _names.GetSZArrayType(FieldType(bytes, ref position, solutionAssemblies)),
            >= 0x02 and <= 0x0E => _names.GetPrimitiveType((PrimitiveTypeCode)code),
            _ => throw new BadImageFormatException("Invalid attribute field type."),
        };
    }

    private string SerializedType(string name, IReadOnlySet<string>? solutionAssemblies) => solutionAssemblies is null
        ? _names.SerializedType(name) : _names.SerializedType(name, solutionAssemblies);

    private static ParseState ReadBytes(ParseState state, string type, int width, byte[] bytes)
    {
        if (width > bytes.Length - state.Position)
        {
            throw new BadImageFormatException("Truncated attribute value.");
        }

        return Append(state, state.Position + width, Value("bits", type, Convert.ToHexString(bytes.AsSpan(state.Position, width))));
    }

    private static ParseState Append(ParseState state, int position, string value) => state with
    {
        Position = position,
        Value = state.Value + value,
    };

    private static int ReadInteger(byte[] bytes, ref int position, int width)
    {
        if (position < 0 || width > bytes.Length - position)
        {
            throw new BadImageFormatException("Truncated custom attribute.");
        }

        var value = 0;
        for (var i = 0; i < width; i++)
        {
            value |= bytes[position++] << (i * 8);
        }

        return value;
    }

    private static string? ReadString(byte[] bytes, ref int position, bool preserveSurrogates = false)
    {
        var first = ReadInteger(bytes, ref position, 1);
        if (first == 0xFF)
        {
            return null;
        }

        position--;
        var length = ReadCompressedInteger(bytes, ref position);

        if (length > bytes.Length - position)
        {
            throw new BadImageFormatException("Truncated serialized string.");
        }

        var value = bytes.AsSpan(position, length);
        position += length;
        return preserveSurrogates ? DecodeStringValue(value) : _utf8.GetString(value);
    }

    private static string DecodeStringValue(ReadOnlySpan<byte> bytes)
    {
        // Roslyn serializes lone UTF-16 surrogates as three-byte sequences in attribute string values.
        // Keep those code units distinct from U+FFFD and validate the rest as strict UTF-8.
        StringBuilder? decoded = null;
        var start = 0;
        for (var offset = 0; offset < bytes.Length - 2; offset++)
        {
            if (bytes[offset] != 0xED
                || bytes[offset + 1] is < 0xA0 or > 0xBF
                || bytes[offset + 2] is < 0x80 or > 0xBF)
            {
                continue;
            }

            decoded ??= new StringBuilder();
            decoded.Append(_utf8.GetString(bytes[start..offset]));
            decoded.Append((char)(0xD000 | ((bytes[offset + 1] & 0x3F) << 6) | (bytes[offset + 2] & 0x3F)));
            offset += 2;
            start = offset + 1;
        }

        var tail = _utf8.GetString(bytes[start..]);
        return decoded is null ? tail : decoded.Append(tail).ToString();
    }

    private static int ReadCompressedInteger(byte[] bytes, ref int position)
    {
        var first = ReadInteger(bytes, ref position, 1);
        return (first & 0x80) == 0 ? first
            : (first & 0xC0) == 0x80
                ? ((first & 0x3F) << 8) | ReadInteger(bytes, ref position, 1)
                : (first & 0xE0) == 0xC0
                    ? ((first & 0x1F) << 24) | (ReadInteger(bytes, ref position, 1) << 16)
                        | (ReadInteger(bytes, ref position, 1) << 8) | ReadInteger(bytes, ref position, 1)
                    : throw new BadImageFormatException("Invalid compressed integer.");
    }

    private static void CheckCount(int count)
    {
        // Reject an adversarially ambiguous blob instead of spending unbounded time or returning a partial value.
        if (count > 4096)
        {
            throw new BadImageFormatException("The custom attribute has too many ambiguous enum interpretations.");
        }
    }

    private sealed record ParseState(int Position, string Value, ImmutableDictionary<string, int> EnumWidths,
        IReadOnlySet<string>? SolutionAssemblies = null);

    private const string SystemType = "attribute-system-type";
    private const string NullObject = "attribute-null-object";
    private readonly MetadataReader _metadata;
    private readonly IlFingerprintNames _names;
    private static readonly UTF8Encoding _utf8 = new(false, true);
}
