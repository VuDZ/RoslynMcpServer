using System.Reflection;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

/// <summary>
/// Reads <see cref="DocumentInfo.IsGenerated"/> from a live document.
/// MSBuildWorkspace sets that flag from the evaluated item ("a side effect of the build").
/// <c>Solution.AddDocument</c> can set the same flag, but the live <see cref="TextDocument"/> keeps it on internal state.
/// A missing member is unknown provenance, not a guess that the file is user code or generated.
/// </summary>
internal static class RoslynDocumentBuildSideEffect
{
    public static bool? TryGetIsGenerated(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var state = ReadMember(document, "State");
        if (state is null)
        {
            return null;
        }

        if (ReadMember(state, "IsGenerated") is bool generated)
        {
            return generated;
        }

        var attributes = ReadMember(state, "Attributes");
        if (attributes is not null && ReadMember(attributes, "IsGenerated") is bool fromAttributes)
        {
            return fromAttributes;
        }

        return null;
    }

    private static object? ReadMember(object instance, string name)
    {
        for (var type = instance.GetType(); type is not null; type = type.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var property = type.GetProperty(name, flags);
            if (property is not null)
            {
                return property.GetValue(instance);
            }

            var field = type.GetField(name, flags);
            if (field is not null)
            {
                return field.GetValue(instance);
            }
        }

        return null;
    }
}
