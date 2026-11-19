using System.Reflection;
using System.Reflection.Emit;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>Operand kind of every IL opcode, taken from the runtime's own opcode table.</summary>
internal static class IlOperandSizes
{
    /// <summary>Returns false for a byte that is not an opcode: the caller must stop reading the body.</summary>
    public static bool TryGet(bool twoByte, byte code, out OperandType operand)
    {
        var known = (twoByte ? _twoByte : _oneByte)[code];
        operand = known ?? default;
        return known is not null;
    }

    private static OperandType?[] Table(bool twoByte)
    {
        var table = new OperandType?[256];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode code && (code.Size == 2) == twoByte)
            {
                table[code.Value & 0xFF] = code.OperandType;
            }
        }

        return table;
    }

    private static readonly OperandType?[] _oneByte = Table(twoByte: false);
    private static readonly OperandType?[] _twoByte = Table(twoByte: true);
}
