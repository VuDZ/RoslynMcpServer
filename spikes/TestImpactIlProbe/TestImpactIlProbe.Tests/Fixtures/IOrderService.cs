namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>A service contract without method bodies: its file is known only from the PDB type table.</summary>
internal interface IOrderService
{
    int Count();
}
