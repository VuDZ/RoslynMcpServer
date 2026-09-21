using System.Text;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

/// <summary>
/// Formats <c>find_implementations</c> results after SymbolFinder has already run
/// (remap and sanitizer retry stay in the tool method).
/// </summary>
internal static class ImplementationListingFormatter
{
    public sealed record RelatedTypeLine(
        string Display,
        string? FilePath,
        int Line,
        int Column,
        string? PreviewLine);

    public sealed record BaseSection(
        string Fqn,
        string KindLabel,
        string SearchMode,
        string MinimallyQualifiedDisplay,
        string FullyQualifiedDisplay,
        IReadOnlyList<RelatedTypeLine> RelatedTypes);

    public static string GetTypeKindLabel(INamedTypeSymbol type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Struct => "struct",
            TypeKind.Class when type.IsRecord => "record",
            TypeKind.Class => type.IsAbstract ? "abstract class" : "class",
            _ => type.TypeKind.ToString().ToLowerInvariant()
        };
    }

    public static string GetSearchMode(TypeKind typeKind, bool transitive) => typeKind switch
    {
        TypeKind.Interface => transitive ? "implementations (transitive)" : "direct implementations",
        TypeKind.Class or TypeKind.Struct => transitive ? "derived types (transitive)" : "direct derived types",
        _ => "related types"
    };

    public static RelatedTypeLine ToRelatedTypeLine(INamedTypeSymbol type, string? previewLine = null)
    {
        ArgumentNullException.ThrowIfNull(type);

        var display = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var location = type.Locations.FirstOrDefault(l => l.IsInSource && l.SourceTree?.FilePath is not null);
        if (location is null)
        {
            return new RelatedTypeLine(display, FilePath: null, Line: 0, Column: 0, previewLine);
        }

        var span = location.GetLineSpan();
        return new RelatedTypeLine(
            display,
            location.SourceTree!.FilePath,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            previewLine);
    }

    public static void AppendSingleTypeHeader(StringBuilder sb, string trimmedName, BaseSection section)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(section);

        sb.AppendLine($"## {section.SearchMode} for `{trimmedName}`");
        sb.AppendLine();
        sb.AppendLine($"**Base symbol:** `{section.MinimallyQualifiedDisplay}` ({section.KindLabel})");
        sb.AppendLine($"`{section.FullyQualifiedDisplay}`");
        sb.AppendLine();
    }

    public static void AppendMultiTypeHeader(
        StringBuilder sb,
        string trimmedName,
        int baseCount,
        int relatedCount)
    {
        ArgumentNullException.ThrowIfNull(sb);

        sb.AppendLine($"## implementations for `{trimmedName}`");
        sb.AppendLine();
        sb.AppendLine($"Found **{baseCount}** matching base type(s), **{relatedCount}** type(s).");
        sb.AppendLine();
    }

    public static List<string> BuildNumberedRelatedLines(IReadOnlyList<RelatedTypeLine> relatedTypes)
    {
        ArgumentNullException.ThrowIfNull(relatedTypes);

        var lines = new List<string>(relatedTypes.Count);
        var index = 1;
        foreach (var related in relatedTypes)
        {
            lines.Add(FormatNumberedLine(index, related));
            index++;
        }

        return lines;
    }

    /// <summary>
    /// One section per base FQN. Empty sections include an explicit "(no implementations)" line.
    /// </summary>
    public static List<string> BuildMultiSectionLines(IReadOnlyList<BaseSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var lines = new List<string>();
        foreach (var section in sections)
        {
            lines.Add($"### `{section.Fqn}` ({section.KindLabel}) — {section.SearchMode}");
            if (section.RelatedTypes.Count == 0)
            {
                lines.Add("(no implementations)");
                continue;
            }

            var index = 1;
            foreach (var related in section.RelatedTypes)
            {
                lines.Add(FormatNumberedLine(index, related));
                index++;
            }
        }

        return lines;
    }

    private static string FormatNumberedLine(int index, RelatedTypeLine related)
    {
        if (related.FilePath is null)
        {
            return $"{index}. `{related.Display}` — (no in-source location)";
        }

        var loc = NavigationListingHelper.FormatLocationLine(
            related.FilePath,
            related.Line,
            related.Column,
            related.PreviewLine);
        return $"{index}. `{related.Display}` — {loc}";
    }
}
