namespace AffectedTestsProbe;

internal sealed class TestClassModel
{
    public TestClassModel(ProjectKey project, string runtimeName)
    {
        Project = project;
        RuntimeName = runtimeName;
    }

    public ProjectKey Project { get; }

    public string RuntimeName { get; }

    public List<TestUnit> Units { get; } = new();

    public List<MemberNode> ClassWideRoots { get; } = new();

    public Dictionary<TestUnit, List<MemberNode>> DataRoots { get; } = new();
}
