namespace TestImpactIlProbe;

/// <summary>Turns a source path recorded in a PDB into a path on this machine.</summary>
internal sealed class DocumentPaths
{
    public DocumentPaths(IReadOnlyList<string> roots)
    {
        _roots = roots;
    }

    public string Map(string document)
    {
        // A deterministic build records sources as "/_/relative/path" instead of the real directory.
        if (!document.StartsWith(DeterministicRoot, StringComparison.Ordinal))
        {
            return document;
        }

        foreach (var root in _roots)
        {
            var candidate = Path.GetFullPath(Path.Combine(root, document[DeterministicRoot.Length..]));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return document;
    }

    private const string DeterministicRoot = "/_/";

    private readonly IReadOnlyList<string> _roots;
}
