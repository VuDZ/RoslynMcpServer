using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RoslynMcpServer.Services.Testing.Il;

internal static class IlFingerprintEncoding
{
    public static string Value(string kind, params object?[] values)
    {
        var text = new StringBuilder(kind);
        text.Append('(');
        foreach (var value in values)
        {
            var part = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            text.Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part);
        }

        return text.Append(')').ToString();
    }

    public static string Sequence(string kind, IEnumerable<string> values) => Value(kind, values.Cast<object?>().ToArray());

    public static string Set(string kind, IEnumerable<string> values) => Sequence(kind, values.Order(StringComparer.Ordinal));

    public static string Hash(string value)
    {
        // CLR strings can contain unpaired surrogates; an encoding fallback would merge distinct literals.
        var bytes = new byte[checked(value.Length * 2)];
        for (var i = 0; i < value.Length; i++)
        {
            bytes[2 * i] = (byte)value[i];
            bytes[2 * i + 1] = (byte)(value[i] >> 8);
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
