namespace AffectedTestsProbe;

/// <summary>
/// Maps a VSTest fully qualified name onto a runtime unit. Display names are not accepted.
/// NUnit appends arguments; xUnit and MSTest theories share one name. Nested types use '+'.
/// </summary>
internal static class FqnMapper
{
    public static TestUnit? Map(string fullyQualifiedName, IReadOnlyList<TestUnit> units)
    {
        var withoutArguments = CutArguments(fullyQualifiedName);
        var dot = withoutArguments.LastIndexOf('.');
        if (dot <= 0)
        {
            return null;
        }

        var typeName = withoutArguments[..dot];
        var methodName = withoutArguments[(dot + 1)..];
        var matches = units.Where(unit => unit.RuntimeName == typeName && unit.MethodName == methodName).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string CutArguments(string name)
    {
        var paren = name.IndexOf('(');
        return paren < 0 ? name : name[..paren];
    }
}
