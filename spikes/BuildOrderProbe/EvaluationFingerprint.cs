using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Evaluation;

namespace BuildOrderProbe;

/// <summary>
/// Hashes of what an evaluation produced, to tell whether two evaluation contexts gave the same project: the
/// resolved imports in order (where SDK resolution lands shows here), the properties by name and the items in
/// order with their metadata; and of what GetAllGlobs returned.
/// </summary>
internal static class EvaluationFingerprint
{
    public static string Of(Project project)
    {
        var text = new StringBuilder();
        foreach (var import in project.Imports)
        {
            text.Append("import: ").Append(import.ImportedProject.FullPath).Append('\n');
        }

        foreach (var property in project.Properties.OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase))
        {
            text.Append("property: ").Append(property.Name).Append('=').Append(property.EvaluatedValue).Append('\n');
        }

        foreach (var item in project.AllEvaluatedItems)
        {
            text.Append(item.ItemType).Append(": ").Append(item.EvaluatedInclude);
            foreach (var metadata in item.Metadata.OrderBy(metadata => metadata.Name, StringComparer.OrdinalIgnoreCase))
            {
                text.Append(" | ").Append(metadata.Name).Append('=').Append(metadata.EvaluatedValue);
            }

            text.Append('\n');
        }

        return Hash(text);
    }

    /// <remarks>
    /// MSBuild builds Excludes through a hash set, so their order changes between processes (BuildHost/task-04);
    /// Excludes and Removes are sorted, everything else keeps its order.
    /// </remarks>
    public static string Of(IEnumerable<GlobResult> globs)
    {
        var text = new StringBuilder();
        foreach (var glob in globs)
        {
            text.Append(glob.ItemElement.ItemType).Append(": ").AppendJoin(';', glob.IncludeGlobs)
                .Append(" | excludes ").AppendJoin(';', glob.Excludes.Order(StringComparer.Ordinal))
                .Append(" | removes ").AppendJoin(';', glob.Removes.Order(StringComparer.Ordinal))
                .Append('\n');
        }

        return Hash(text);
    }

    private static string Hash(StringBuilder text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
}
