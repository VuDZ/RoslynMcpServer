using System.Text;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

/// <summary>
/// Formats in-source definition locations for <c>find_symbol_definition</c>
/// (minimally qualified display, FQN, file, 1-based line and column).
/// </summary>
internal static class DefinitionLocationFormatter
{
    public static string FormatLocation(ISymbol symbol, Location location)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(location);

        var sb = new StringBuilder();
        AppendLocation(sb, symbol, location);
        return sb.ToString().TrimEnd();
    }

    public static string FormatNoSourceLocations(ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var sb = new StringBuilder();
        AppendNoSourceLocations(sb, symbol);
        return sb.ToString().TrimEnd();
    }

    public static void AppendLocation(StringBuilder sb, ISymbol symbol, Location location)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(location);

        var display = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var fqn = SymbolDeclarationResolver.GetSymbolFqn(symbol);
        var path = location.SourceTree?.FilePath ?? "(unknown)";
        var linePos = location.GetLineSpan().StartLinePosition;

        sb.AppendLine($"Symbol: {display}");
        sb.AppendLine($"  Full name: {fqn}");
        sb.AppendLine($"  File: {path}");
        sb.AppendLine($"  Line: {linePos.Line + 1}");
        sb.AppendLine($"  Column: {linePos.Character + 1}");
        sb.AppendLine();
    }

    public static void AppendNoSourceLocations(StringBuilder sb, ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(symbol);

        var display = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        sb.AppendLine($"Symbol: {display}");
        sb.AppendLine($"  Full name: {SymbolDeclarationResolver.GetSymbolFqn(symbol)}");
        sb.AppendLine("  (no in-source locations — metadata or implicit declaration only)");
        sb.AppendLine();
    }
}
