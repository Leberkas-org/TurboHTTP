namespace TurboHTTP.Server;

internal sealed record RouteEntry;

public sealed record RouteMatchResult(bool IsMatch, IRouteDispatcher? Dispatcher, IDictionary<string, string>? RouteValues, object? Metadata);

public interface IRouteDispatcher
{
    Task DispatchAsync(TurboHttpContext context, CancellationToken cancellationToken);
}

public abstract class RouteTable
{
    public virtual RouteMatchResult Match(string method, string path) => new(false, null, null, null);
}

public sealed class TurboRouteTable : RouteTable
{
    public TurboRouteTable Freeze() => this;
}
