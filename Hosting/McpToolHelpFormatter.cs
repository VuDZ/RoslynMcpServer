using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace RoslynMcpServer.Hosting;

public static class McpToolHelpFormatter
{
    public static string FormatGroups(McpToolActivationService activation)
    {
        ArgumentNullException.ThrowIfNull(activation);

        var activeGroups = activation.ActiveGroups.ToHashSet(StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.AppendLine("# Tool groups");
        sb.AppendLine();
        sb.AppendLine($"Profile: `{activation.Profile}`.");
        sb.AppendLine($"Startup groups: {activation.FormatStartupGroupsMarkdown()}.");
        sb.AppendLine($"Dynamic groups: {activation.FormatDynamicGroupsMarkdown()}.");
        sb.AppendLine();
        sb.AppendLine("Call `enable_tool_group` to add one group to this session. Clients that ignore `tools/list_changed` should restart with `ROSLYN_MCP_TOOL_PROFILE=lite` and `ROSLYN_MCP_TOOL_GROUPS=decompile,nuget`. In `full`, extra groups are recorded but do not change the set.");
        sb.AppendLine();

        foreach (var group in McpToolGroups.All)
        {
            var tools = McpToolCatalog.All.Where(d => d.Group == group).Select(d => d.Name).ToArray();
            var active = activeGroups.Contains(group);
            sb.AppendLine($"## `{group}`");
            sb.AppendLine();
            sb.AppendLine($"- Purpose: {McpToolGroups.Describe(group)}");
            sb.AppendLine($"- Active: {(active ? "yes" : "no")}");
            sb.AppendLine($"- Tools: {string.Join(", ", tools.Select(n => $"`{n}`"))}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatToolHelp(string? toolName, McpToolActivationService activation)
    {
        ArgumentNullException.ThrowIfNull(activation);

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return FormatUnknown(toolName, activation);
        }

        var trimmed = toolName.Trim();
        var descriptor = McpToolCatalog.All.FirstOrDefault(d => d.Name.Equals(trimmed, StringComparison.Ordinal));
        if (descriptor is null)
        {
            return FormatUnknown(trimmed, activation);
        }

        McpToolHelpCatalog.TryGet(descriptor.Name, out var extra);
        extra ??= new McpToolHelpEntry();

        var registered = activation.IsToolActive(descriptor.Name);
        var purpose = descriptor.Method.GetCustomAttribute<DescriptionAttribute>()?.Description?.Trim();
        var parameters = ReadParameters(descriptor.Method);

        var sb = new StringBuilder();
        sb.AppendLine($"# `{descriptor.Name}`");
        sb.AppendLine();
        sb.AppendLine($"- Kind: {descriptor.Classification}");
        sb.AppendLine($"- Group: `{descriptor.Group}` ({(registered ? "active" : "inactive")})");
        if (!string.IsNullOrWhiteSpace(extra.Prerequisites))
        {
            sb.AppendLine($"- Prerequisites: {extra.Prerequisites}");
        }

        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(purpose))
        {
            sb.AppendLine(purpose);
            sb.AppendLine();
        }

        sb.AppendLine("## Parameters");
        sb.AppendLine();
        if (parameters.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            foreach (var parameter in parameters)
            {
                sb.Append("- `").Append(parameter.Name).Append("` (").Append(parameter.JsonType);
                if (parameter.Required)
                {
                    sb.Append(", required");
                }
                else
                {
                    sb.Append(", optional");
                    if (parameter.DefaultDisplay is not null)
                    {
                        sb.Append(", default ").Append(parameter.DefaultDisplay);
                    }
                }

                sb.Append(')');
                if (!string.IsNullOrWhiteSpace(parameter.Description))
                {
                    sb.Append(": ").Append(parameter.Description);
                }

                sb.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(extra.Workflow))
        {
            sb.AppendLine();
            sb.AppendLine("## Workflow");
            sb.AppendLine();
            sb.AppendLine(extra.Workflow);
        }

        if (!string.IsNullOrWhiteSpace(extra.Pitfalls))
        {
            sb.AppendLine();
            sb.AppendLine("## Pitfalls");
            sb.AppendLine();
            sb.AppendLine(extra.Pitfalls);
        }

        if (extra.RelatedTools.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Related");
            sb.AppendLine();
            foreach (var related in extra.RelatedTools)
            {
                sb.AppendLine($"- `{related}`");
            }
        }

        return sb.ToString().TrimEnd();
    }

    internal static IReadOnlyList<McpToolParameterHelp> ReadParameters(MethodInfo method)
    {
        var list = new List<McpToolParameterHelp>();
        foreach (var parameter in method.GetParameters())
        {
            if (parameter.ParameterType == typeof(CancellationToken) || parameter.Name is null)
            {
                continue;
            }

            list.Add(new McpToolParameterHelp
            {
                Name = parameter.Name,
                JsonType = ToJsonType(parameter.ParameterType),
                Required = !parameter.HasDefaultValue,
                DefaultDisplay = parameter.HasDefaultValue ? FormatDefault(parameter.DefaultValue) : null,
                Description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description,
            });
        }

        return list;
    }

    private static string FormatUnknown(string? toolName, McpToolActivationService activation)
    {
        var names = McpToolCatalog.All.Select(d => d.Name).ToArray();
        var suggestions = SuggestNames(toolName, names);
        var sb = new StringBuilder();
        sb.Append("Unknown tool");
        if (!string.IsNullOrWhiteSpace(toolName))
        {
            sb.Append(" `").Append(toolName.Trim()).Append('`');
        }

        sb.Append('.');
        if (suggestions.Count > 0)
        {
            sb.Append(" Close names: ").Append(string.Join(", ", suggestions.Select(n => $"`{n}`"))).Append('.');
        }

        sb.Append(" Call `list_tool_groups` for the catalog in profile `").Append(activation.Profile).Append("`.");
        return sb.ToString();
    }

    internal static IReadOnlyList<string> SuggestNames(string? query, IReadOnlyList<string> names)
    {
        if (string.IsNullOrWhiteSpace(query) || names.Count == 0)
        {
            return names.Take(5).ToArray();
        }

        var needle = query.Trim();
        return names
            .Select(name => (name, distance: Distance(needle, name)))
            .OrderBy(x => x.distance)
            .ThenBy(x => x.name, StringComparer.Ordinal)
            .Where(x => x.distance <= Math.Max(4, needle.Length / 2))
            .Take(5)
            .Select(x => x.name)
            .ToArray();
    }

    private static string ToJsonType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(string))
        {
            return "string";
        }

        if (underlying == typeof(bool))
        {
            return "boolean";
        }

        if (underlying == typeof(int) || underlying == typeof(long))
        {
            return "integer";
        }

        if (underlying.IsArray)
        {
            return "array";
        }

        return "object";
    }

    private static string FormatDefault(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is bool b)
        {
            return b ? "true" : "false";
        }

        if (value is string s)
        {
            return string.IsNullOrEmpty(s) ? "\"\"" : $"`{s}`";
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "null";
        }

        return value.ToString() ?? "null";
    }

    private static int Distance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        var n = a.Length;
        var m = b.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }
}

public sealed class McpToolParameterHelp
{
    public required string Name { get; init; }
    public required string JsonType { get; init; }
    public required bool Required { get; init; }
    public string? DefaultDisplay { get; init; }
    public string? Description { get; init; }
}
