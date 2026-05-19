using TurboHTTP.Server;
using TurboHTTP.Server.Binding;

namespace TurboHTTP.Routing.Builder;

public sealed class TurboEntityBuilder<TKey>
{
    private readonly string _pattern;
    private readonly Dictionary<HttpMethod, TurboEntityMethodBuilder> _methods = new();
    private readonly EntityResponseMapperCollection _responseMappers = new();
    private TimeSpan _timeout = TimeSpan.FromSeconds(5);
    private string? _entityKeyParam;
    private Func<IServiceProvider, IEntityActorResolver>? _resolverFactory;

    public TurboEntityBuilder(string pattern)
    {
        _pattern = pattern;
    }

    public TurboEntityMethodBuilder OnGet(Delegate messageFactory)
        => AddMethod(HttpMethod.Get, messageFactory);

    public TurboEntityMethodBuilder OnPost(Delegate messageFactory)
        => AddMethod(HttpMethod.Post, messageFactory);

    public TurboEntityMethodBuilder OnPut(Delegate messageFactory)
        => AddMethod(HttpMethod.Put, messageFactory);

    public TurboEntityMethodBuilder OnDelete(Delegate messageFactory)
        => AddMethod(HttpMethod.Delete, messageFactory);

    public TurboEntityMethodBuilder OnPatch(Delegate messageFactory)
        => AddMethod(HttpMethod.Patch, messageFactory);

    public TurboEntityBuilder<TKey> MapResponse<TResponse>(Func<TurboHttpContext, TResponse, Task> mapper)
    {
        _responseMappers.Add(mapper);
        return this;
    }

    public TurboEntityBuilder<TKey> WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    public TurboEntityBuilder<TKey> WithEntityKey(string paramName)
    {
        _entityKeyParam = paramName;
        return this;
    }

    public TurboEntityBuilder<TKey> UseResolver<TResolver>() where TResolver : IEntityActorResolver, new()
    {
        _resolverFactory = _ => new TResolver();
        return this;
    }

    internal void AddToRouteTable(TurboRouteTable table)
    {
        var entityKeyParam = _entityKeyParam ?? ExtractLastParam(_pattern);

        foreach (var kv in _methods)
        {
            var methodConfig = kv.Value.ToConfig();
            var dispatcher = new EntityDispatcher(
                entityKeyParam,
                methodConfig,
                _responseMappers,
                _timeout,
                _resolverFactory);
            table.AddWithDispatcher(kv.Key, _pattern, dispatcher);
        }
    }

    private TurboEntityMethodBuilder AddMethod(HttpMethod method, Delegate messageFactory)
    {
        var bound = DelegateHandlerBinder.BindEntityDelegate(_pattern, messageFactory);
        var builder = new TurboEntityMethodBuilder(bound);
        _methods[method] = builder;
        return builder;
    }

    private static string ExtractLastParam(string pattern)
    {
        var segments = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (segments[i].StartsWith('{') && segments[i].EndsWith('}'))
            {
                return segments[i][1..^1];
            }
        }

        return "id";
    }
}