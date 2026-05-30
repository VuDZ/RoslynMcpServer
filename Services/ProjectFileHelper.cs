using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

public static class ProjectFileHelper
{
    public static async Task<string> AddPackageReferenceAsync(
        string projectPath,
        string packageId,
        string? version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package id is empty.");
        }

        var fullPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullPath) || !fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path must be an existing .csproj file: `{fullPath}`.");
        }

        var doc = XDocument.Load(fullPath, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidOperationException("Invalid .csproj root.");
        var ns = root.Name.Namespace;

        var existing = root.Descendants(ns + "PackageReference")
            .FirstOrDefault(e => string.Equals((string?)e.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(version))
            {
                existing.SetAttributeValue("Version", version.Trim());
                await File.WriteAllTextAsync(fullPath, doc.ToString(), cancellationToken).ConfigureAwait(false);
                return $"Updated PackageReference `{packageId}` to version `{version.Trim()}` in `{fullPath}`.";
            }

            return $"PackageReference `{packageId}` already exists in `{fullPath}`.";
        }

        var itemGroup = root.Elements(ns + "ItemGroup")
            .FirstOrDefault(g => g.Elements(ns + "PackageReference").Any())
            ?? new XElement(ns + "ItemGroup");

        if (!root.Elements(ns + "ItemGroup").Contains(itemGroup))
        {
            root.Add(itemGroup);
        }

        var packageRef = new XElement(ns + "PackageReference");
        packageRef.SetAttributeValue("Include", packageId.Trim());
        if (!string.IsNullOrWhiteSpace(version))
        {
            packageRef.SetAttributeValue("Version", version.Trim());
        }

        itemGroup.Add(new XText(Environment.NewLine + "    "), packageRef, new XText(Environment.NewLine + "  "));
        await File.WriteAllTextAsync(fullPath, doc.ToString(), cancellationToken).ConfigureAwait(false);
        return $"Added PackageReference `{packageId}`{(string.IsNullOrWhiteSpace(version) ? string.Empty : $" v{version.Trim()}")} to `{fullPath}`.";
    }

    public static async Task<string> RemovePackageReferenceAsync(
        string projectPath,
        string packageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package id is empty.");
        }

        var fullPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Project file not found.", fullPath);
        }

        var doc = XDocument.Load(fullPath, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidOperationException("Invalid .csproj root.");
        var ns = root.Name.Namespace;

        var target = root.Descendants(ns + "PackageReference")
            .FirstOrDefault(e => string.Equals((string?)e.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            throw new InvalidOperationException($"PackageReference `{packageId}` was not found in `{fullPath}`.");
        }

        target.Remove();
        await File.WriteAllTextAsync(fullPath, doc.ToString(), cancellationToken).ConfigureAwait(false);
        return $"Removed PackageReference `{packageId}` from `{fullPath}`.";
    }
}
