namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>Names the contract only, as code that gets its dependency from a container does.</summary>
internal sealed class OrderConsumer
{
    public OrderConsumer(IOrderService service)
    {
        _service = service;
    }

    public int Total() => _service.Count();

    private readonly IOrderService _service;
}
