using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public sealed class TurboRouteGroupBuilder
{
    private readonly string _prefix;
    private readonly TurboRouteTable _table;

    internal TurboRouteGroupBuilder(string prefix, TurboRouteTable table)
    {
        _prefix = prefix;
        _table = table;
    }

    public TurboRouteHandlerBuilder MapGet(string pattern, Delegate handler)
    {
        return _table.Add(HttpMethod.Get, _prefix + pattern, handler);
    }

    public TurboRouteHandlerBuilder MapPost(string pattern, Delegate handler)
    {
        return _table.Add(HttpMethod.Post, _prefix + pattern, handler);
    }

    public TurboRouteHandlerBuilder MapPut(string pattern, Delegate handler)
    {
        return _table.Add(HttpMethod.Put, _prefix + pattern, handler);
    }

    public TurboRouteHandlerBuilder MapDelete(string pattern, Delegate handler)
    {
        return _table.Add(HttpMethod.Delete, _prefix + pattern, handler);
    }

    public TurboRouteHandlerBuilder MapPatch(string pattern, Delegate handler)
    {
        return _table.Add(HttpMethod.Patch, _prefix + pattern, handler);
    }

    public TurboRouteHandlerBuilder MapMethods(string pattern, IEnumerable<HttpMethod> methods, Delegate handler)
    {
        TurboRouteHandlerBuilder? last = null;
        foreach (var method in methods)
        {
            last = _table.Add(method, _prefix + pattern, handler);
        }

        return last!;
    }

    public TurboRouteGroupBuilder MapGroup(string prefix)
    {
        return new TurboRouteGroupBuilder(_prefix + prefix, _table);
    }

    public TurboRouteGroupBuilder WithTags(params string[] tags)
    {
        return this;
    }

    public TurboRouteGroupBuilder WithMetadata(params object[] metadata)
    {
        return this;
    }

    public TurboRouteGroupBuilder RequireAuthorization()
    {
        return this;
    }

    public TurboRouteGroupBuilder AllowAnonymous()
    {
        return this;
    }

    public TurboRouteHandlerBuilder MapEntity(string pattern, Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(_prefix + pattern);
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(_table);
        return new TurboRouteHandlerBuilder();
    }

    public TurboRouteHandlerBuilder MapEntity<TActorKey>(string pattern, Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(_prefix + pattern).UseActorRef(x => x.Get<TActorKey>());
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(_table);
        return new TurboRouteHandlerBuilder();
    }
}