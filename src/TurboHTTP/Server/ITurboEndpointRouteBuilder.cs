using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public interface ITurboEndpointRouteBuilder
{
    IServiceProvider ServiceProvider { get; }

    TurboRouteTable RouteTable { get; }
}
