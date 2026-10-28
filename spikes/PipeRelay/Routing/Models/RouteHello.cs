namespace PipeRelay.Routing.Models;

internal sealed record RouteHello(string Identity, string Key, string Descriptor, Guid Generation);
