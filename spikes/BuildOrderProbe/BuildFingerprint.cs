using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Execution;

namespace BuildOrderProbe;

/// <summary>
/// A hash of what the workspace takes from a design-time build: the result, the target path, and the items the
/// BuildHost extracts, in their order, with the metadata that changes how a reference or a file is used. Two orders
/// of building are equivalent for this probe when every project and target framework has the same hash.
/// </summary>
internal static class BuildFingerprint
{
    public static string Of(BuildResultCode result, ProjectInstance state)
    {
        var text = new StringBuilder();
        text.Append("result=").Append(result).Append('\n');
        text.Append("TargetPath=").Append(state.GetPropertyValue("TargetPath")).Append('\n');
        foreach (var type in _itemTypes)
        {
            foreach (var item in state.GetItems(type))
            {
                text.Append(type).Append(": ").Append(item.EvaluatedInclude);
                foreach (var name in _metadataNames)
                {
                    var value = item.GetMetadataValue(name);
                    if (value.Length > 0)
                    {
                        text.Append(" | ").Append(name).Append('=').Append(value);
                    }
                }

                text.Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static readonly string[] _itemTypes =
    [
        "CscCommandLineArgs",
        "VbcCommandLineArgs",
        "Compile",
        "ReferencePath",
        "ProjectReference",
        "Analyzer",
        "AdditionalFiles",
        "EditorConfigFiles",
    ];

    private static readonly string[] _metadataNames =
    [
        "Aliases",
        "EmbedInteropTypes",
        "Link",
        "OutputItemType",
        "ReferenceOutputAssembly",
        "ReferenceSourceTarget",
        "MSBuildSourceProjectFile",
        "MSBuildSourceTargetName",
    ];
}
