using System.Text;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace RoslynMcpServer.Services.Editing;

internal static class ProjectRenameProject
{
    internal static (string? Text, bool UpdateAssemblyName, bool UpdateRootNamespace) Rewrite(
        ProjectRenameFile file,
        ProjectMove move,
        bool renameProperties)
    {
        using var collection = new ProjectCollection();
        using var reader = file.CreateXmlReader();
        ProjectRootElement project;
        try
        {
            // Match MSBuild's normal construction semantics. The source snapshot preserves formatting for the edits.
            project = ProjectRootElement.Create(reader, collection, preserveFormatting: false);
        }
        catch (InvalidProjectFileException ex) when (ex.ErrorCode == "MSB4025" && !move.Contains(file.Path))
        {
            // The previous planner also ignored malformed XML in external projects under the search root.
            return default;
        }

        var edits = new ProjectRenameTextEdits(file);
        var references = project.AllChildren.OfType<ProjectItemElement>()
            .Where(item => item.ItemType.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        RewriteReferences(file, move, references, edits);

        var (assemblyName, rootNamespace) = renameProperties
            ? RewriteNameProperties(file, move, project, edits)
            : (false, false);
        return (edits.GetUpdatedText(), assemblyName, rootNamespace);
    }

    private static void RewriteReferences(
        ProjectRenameFile file,
        ProjectMove move,
        ProjectItemElement[] references,
        ProjectRenameTextEdits edits)
    {
        var includes = references
            .Where(item => !string.IsNullOrWhiteSpace(item.Include) && ProjectRenameHelper.IsLiteralProjectReference(item.Include))
            .Select(item =>
            {
                var oldPath = ProjectMove.ResolveReference(file.Path, item.Include.Trim());
                return new ProjectReferenceInclude(item, oldPath, move.FinalPath(oldPath));
            })
            .ToArray();

        foreach (var item in references)
        {
            RewriteOperand(item, "Include", item.Include, item.IncludeLocation);
            RewriteOperand(item, "Exclude", item.Exclude, item.ExcludeLocation);
            RewriteOperand(item, "Update", item.Update, item.UpdateLocation);
            RewriteOperand(item, "Remove", item.Remove, item.RemoveLocation);
        }

        void RewriteOperand(ProjectItemElement item, string name, string value, ElementLocation? location)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            // MSBuild trims whitespace around item specifications before matching them.
            var spec = value.Trim();
            if (!ProjectRenameHelper.IsLiteralProjectReference(spec))
            {
                throw new InvalidOperationException(
                    $"Unsupported ProjectReference path in `{file.Path}`: `{value}`. Use a literal project path before moving the directory.");
            }

            var updated = name == "Include"
                ? move.RewriteReference(file.Path, spec, '\\', out _)
                : RewriteSelector(item, name, spec, includes, file.Path, move);
            if (updated is null)
            {
                return;
            }

            if (location is null)
            {
                throw new InvalidOperationException($"The source location of ProjectReference {name} in `{file.Path}` is unavailable.");
            }

            edits.ReplaceAttribute(location.Line, location.Column, name, value, updated);
        }
    }

    private static string? RewriteSelector(
        ProjectItemElement item,
        string selector,
        string spec,
        ProjectReferenceInclude[] includes,
        string ownerPath,
        ProjectMove move)
    {
        // MSBuild's literal item filters ignore case even on a case-sensitive filesystem.
        // See https://github.com/dotnet/msbuild/blob/main/src/Build/Utilities/FileSpecMatchTester.cs#L69-L88.
        var comparison = StringComparer.OrdinalIgnoreCase;
        var oldPath = ProjectMove.ResolveReference(ownerPath, spec);
        var isExclude = selector == "Exclude";
        var candidates = isExclude ? includes.Where(include => include.Item == item) : includes;
        var matchedTargets = candidates.Where(include => comparison.Equals(include.OldPath, oldPath))
            .Select(include => include.NewPath).Distinct(comparison).Take(2).ToArray();
        var target = matchedTargets.FirstOrDefault() ?? move.FinalPath(oldPath);
        if (matchedTargets.Length > 1
            || (matchedTargets.Length == 0 && !isExclude && move.IsCaseVariantOfMovingPath(oldPath))
            || candidates.Any(include => !comparison.Equals(include.OldPath, oldPath)
                && comparison.Equals(include.NewPath, target)))
        {
            throw new InvalidOperationException(
                $"Ambiguous ProjectReference {selector} in `{ownerPath}`: `{spec}` cannot preserve its item matches after the move.");
        }

        var updated = move.FormatReferenceAfterMove(ownerPath, spec, target, '\\', StringComparison.OrdinalIgnoreCase);
        if ((updated is not null || !string.Equals(oldPath, target, _pathComparison))
            && item.AllParents.Any(parent => parent is ProjectTargetElement))
        {
            // Target-time matching compares evaluated item strings, where absolute and relative specs differ.
            // See https://github.com/dotnet/msbuild/blob/main/src/Build/BackEnd/Components/RequestBuilder/IntrinsicTasks/ItemGroupIntrinsicTask.cs#L531-L589.
            throw new InvalidOperationException(
                $"Unsupported target-time ProjectReference {selector} in `{ownerPath}`: `{spec}` can change its item matches after the move.");
        }

        return updated;
    }

    private static (bool AssemblyName, bool RootNamespace) RewriteNameProperties(
        ProjectRenameFile file,
        ProjectMove move,
        ProjectRootElement project,
        ProjectRenameTextEdits edits)
    {
        var oldName = Path.GetFileNameWithoutExtension(move.OldProjectPath);
        var newName = Path.GetFileNameWithoutExtension(move.NewProjectPath);
        var properties = project.AllChildren.OfType<ProjectPropertyElement>().ToArray();
        var assemblyName = MatchingProperties(properties, "AssemblyName", oldName);
        var rootNamespace = MatchingProperties(properties, "RootNamespace", oldName);
        if (assemblyName.Length + rootNamespace.Length == 0)
        {
            return (false, false);
        }

        var projectStart = file.GetOffset(project.Location.Line, project.Location.Column);
        var pending = new Dictionary<int, ProjectPropertyElement>();
        foreach (var property in assemblyName.Concat(rootNamespace))
        {
            var start = file.GetOffset(property.Location.Line, property.Location.Column);
            // An expanded markup entity can point back into its DTD declaration instead of the project body.
            if (start <= projectStart || !pending.TryAdd(start, property))
            {
                throw UnconfirmedProperty(file, property.Name);
            }
        }

        using var reader = file.CreateXmlReader();
        var location = (IXmlLineInfo)reader;
        while (pending.Count > 0 && reader.Read())
        {
            // Construction positions point at '<', while XmlReader positions point at the name.
            if (reader.NodeType == XmlNodeType.Element
                && pending.Remove(file.GetOffset(location.LineNumber, location.LinePosition) - 1, out var property))
            {
                if (!reader.Name.Equals(property.Name, StringComparison.Ordinal))
                {
                    throw UnconfirmedProperty(file, property.Name);
                }

                RewritePropertyValue(file, reader, property, newName, edits);
            }
        }

        if (pending.Count != 0)
        {
            throw UnconfirmedProperty(file, pending.Values.First().Name);
        }

        return (assemblyName.Length != 0, rootNamespace.Length != 0);
    }

    private static ProjectPropertyElement[] MatchingProperties(
        ProjectPropertyElement[] properties,
        string name,
        string oldName)
    {
        var declarations = properties.Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        // Preserve the existing opt-in rule: the first explicit declaration must match the old filename.
        return declarations.FirstOrDefault()?.Value.Trim() == oldName
            ? declarations.Where(property => property.Value.Trim() == oldName).ToArray()
            : [];
    }

    private static void RewritePropertyValue(
        ProjectRenameFile file,
        XmlReader reader,
        ProjectPropertyElement property,
        string newName,
        ProjectRenameTextEdits edits)
    {
        using var content = reader.ReadSubtree();
        var location = (IXmlLineInfo)content;
        var value = new StringBuilder();
        var pieces = new List<(int Start, int Length, bool CData, bool HasValue)>();
        var previousEnd = file.GetOffset(property.Location.Line, property.Location.Column);
        while (content.Read())
        {
            if (content.Depth != 1 || content.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA
                or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace) || content.Value.Length == 0)
            {
                continue;
            }

            var offset = file.GetOffset(location.LineNumber, location.LinePosition);
            if (offset <= previousEnd)
            {
                throw UnconfirmedProperty(file, property.Name);
            }

            var cdata = content.NodeType == XmlNodeType.CDATA;
            var length = GetPropertyPieceLength(file, offset, cdata, property.Name);
            pieces.Add((offset, length, cdata, !string.IsNullOrWhiteSpace(content.Value)));
            if (content.NodeType != XmlNodeType.Whitespace)
            {
                value.Append(content.Value);
            }
            previousEnd = offset + length;
        }

        var first = pieces.FindIndex(piece => piece.HasValue);
        if (first < 0 || !value.ToString().Trim().Equals(property.Value.Trim(), StringComparison.Ordinal))
        {
            throw UnconfirmedProperty(file, property.Name);
        }

        // Comments can split a scalar into text nodes. Keep the outer whitespace and remove whitespace that belonged to the old name.
        var last = pieces.FindLastIndex(piece => piece.HasValue);
        for (var index = first; index <= last; index++)
        {
            var (start, length, cdata, _) = pieces[index];
            var text = file.Text.AsSpan(start, length);
            var leading = index == first ? length - text.TrimStart().Length : 0;
            var end = index == last ? text.TrimEnd().Length : length;
            var replacement = index == first ? EscapePropertyValue(newName, cdata) : string.Empty;
            edits.Replace(start + leading, end - leading, replacement);
        }
    }

    private static int GetPropertyPieceLength(
        ProjectRenameFile file,
        int offset,
        bool cdata,
        string propertyName)
    {
        const string CDataStart = "<![CDATA[";
        var text = file.Text;
        var end = cdata ? text.IndexOf("]]>", offset, StringComparison.Ordinal) : text.IndexOf('<', offset);
        if (end <= offset || (cdata && !text.AsSpan(0, offset).EndsWith(CDataStart, StringComparison.Ordinal)))
        {
            throw UnconfirmedProperty(file, propertyName);
        }

        return end - offset;
    }

    private static string EscapePropertyValue(string newName, bool cdata)
    {
        XmlConvert.VerifyXmlChars(newName);
        return cdata
            ? newName.Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal)
                .Replace("\r", "]]>&#xD;<![CDATA[", StringComparison.Ordinal)
            : newName.Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\r", "&#xD;", StringComparison.Ordinal);
    }

    private static InvalidOperationException UnconfirmedProperty(ProjectRenameFile file, string name) =>
        new($"Could not confirm the original value of `{name}` in `{file.Path}`.");

    private sealed record ProjectReferenceInclude(ProjectItemElement Item, string OldPath, string NewPath);

    private static readonly StringComparison _pathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
