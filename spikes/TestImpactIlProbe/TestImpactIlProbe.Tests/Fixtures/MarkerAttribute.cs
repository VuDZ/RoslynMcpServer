namespace TestImpactIlProbe.Tests.Fixtures;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class MarkerAttribute : Attribute
{
    public MarkerAttribute(Type type)
    {
        Type = type;
    }

    public Type Type { get; }
}
