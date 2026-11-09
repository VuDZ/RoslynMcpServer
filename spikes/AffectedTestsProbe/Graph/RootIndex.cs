namespace AffectedTestsProbe;

internal sealed class RootIndex
{
    public RootIndex(
        IReadOnlyList<TestClassModel> classes,
        IReadOnlyDictionary<MemberNode, IReadOnlyList<TestUnit>> unitsByRoot,
        IReadOnlyList<string> limitations,
        IReadOnlyList<UnmappedType> unmapped)
    {
        Classes = classes;
        UnitsByRoot = unitsByRoot;
        Limitations = limitations;
        Unmapped = unmapped;
    }

    public IReadOnlyList<TestClassModel> Classes { get; }

    public IReadOnlyDictionary<MemberNode, IReadOnlyList<TestUnit>> UnitsByRoot { get; }

    public IReadOnlyList<string> Limitations { get; }

    public IReadOnlyList<UnmappedType> Unmapped { get; }

    public IReadOnlyList<TestUnit> Units => Classes.SelectMany(testClass => testClass.Units).ToArray();
}
