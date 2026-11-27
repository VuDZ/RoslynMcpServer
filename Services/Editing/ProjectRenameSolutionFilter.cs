using System.Text;
using System.Text.Json;

namespace RoslynMcpServer.Services.Editing;

/// <summary>
/// Rewrites the solution path and project entries of a solution filter (<c>.slnf</c>) after a project move.
/// </summary>
/// <remarks>
/// MSBuild resolves filter entries against the filtered solution's directory and rejects an entry missing from that
/// solution, so a stale entry broke every build of the filter. Only the JSON string values that change are replaced;
/// the rest of the file keeps its text, escapes and layout.
/// </remarks>
internal static class ProjectRenameSolutionFilter
{
    internal static string? Rewrite(ProjectRenameFile file, ProjectMove move)
    {
        var (solution, projects) = ReadEntries(file);
        if (solution is null)
        {
            return null;
        }

        var edits = new ProjectRenameTextEdits(file);
        var solutionPath = ProjectMove.ResolveReference(file.Path, solution.Value.Value);
        Replace(edits, solution.Value, move.FormatReferenceAfterMove(
            file.Path, solution.Value.Value, move.FinalPath(solutionPath), '\\', _pathComparison));
        foreach (var project in projects)
        {
            var target = ProjectMove.ResolveReference(solutionPath, project.Value);
            Replace(edits, project, move.FormatReferenceAfterMove(
                solutionPath, project.Value, move.FinalPath(target), '\\', _pathComparison));
        }

        return edits.GetUpdatedText();
    }

    private static (JsonString? Solution, List<JsonString> Projects) ReadEntries(ProjectRenameFile file)
    {
        var utf8 = Encoding.UTF8.GetBytes(file.Text);
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        var containers = new List<string?>();
        string? property = null;
        JsonString? solution = null;
        var projects = new List<JsonString>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    property = reader.GetString();
                    continue;
                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    containers.Add(property);
                    break;
                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    containers.RemoveAt(containers.Count - 1);
                    break;
                case JsonTokenType.String when IsAt(containers, "solution") && property == "path":
                    solution = ReadString(ref reader, utf8);
                    break;
                case JsonTokenType.String when IsAt(containers, "solution", "projects") && property is null:
                    projects.Add(ReadString(ref reader, utf8));
                    break;
            }

            property = null;
        }

        return (solution, projects);
    }

    private static bool IsAt(List<string?> containers, params string[] names) =>
        containers.Count == names.Length + 1 && containers.Skip(1).SequenceEqual(names, StringComparer.Ordinal);

    private static JsonString ReadString(ref Utf8JsonReader reader, byte[] utf8)
    {
        // TokenStartIndex is the opening quote; ValueSpan is the escaped text between the quotes.
        var start = (int)reader.TokenStartIndex + 1;
        return new JsonString(
            reader.GetString()!,
            Encoding.UTF8.GetCharCount(utf8, 0, start),
            Encoding.UTF8.GetCharCount(utf8, start, reader.ValueSpan.Length));
    }

    private static void Replace(ProjectRenameTextEdits edits, JsonString value, string? updated)
    {
        if (updated is not null)
        {
            edits.Replace(value.Start, value.Length, updated.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal));
        }
    }

    private readonly record struct JsonString(string Value, int Start, int Length);

    private static readonly StringComparison _pathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
