using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Routing;
using TurboHTTP.Routing.Binding;

namespace TurboHTTP.Server;

public sealed class TurboEntityBuilder
{
    private readonly string _pattern;
    private readonly Dictionary<HttpMethod, TurboEntityMethodBuilder> _methods = new();
    private readonly EntityResponseMapperCollection _responseMappers = new();
    private TimeSpan _timeout = TimeSpan.FromSeconds(5);
    private IEntityActorResolver _resolver = new GenericActorRefFactory(_ => ActorRefs.Nobody);

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

    public TurboEntityBuilder Response<TResponse>(Func<TurboHttpContext, TResponse, Task> mapper)
    {
        _responseMappers.Add(mapper);
        return this;
    }

    public TurboEntityBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    public TurboEntityBuilder UseResolver(IEntityActorResolver resolver)
    {
        _resolver = resolver;
        return this;
    }

    public TurboEntityBuilder UseResolver<TResolver>() where TResolver : IEntityActorResolver, new()
        => UseResolver(new TResolver());

    public TurboEntityBuilder UseActorRef<TActorKey>()
        => UseActorRef(x => x.Get<TActorKey>());

    public TurboEntityBuilder UseActorRef(Func<IServiceProvider, IActorRef> factory)
        => UseResolver(new GenericActorRefFactory(factory));

    public TurboEntityBuilder UseActorRef(Func<IReadOnlyActorRegistry, IActorRef> actorRefFactory)
        => UseActorRef(sp => actorRefFactory(sp.GetRequiredService<IReadOnlyActorRegistry>()));

    internal void AddToRouteTable(TurboRouteTable table)
    {
        foreach (var kv in _methods)
        {
            var methodConfig = kv.Value.ToConfig();
            var dispatcher = new EntityDispatcher(
                methodConfig,
                _responseMappers,
                _timeout,
                _resolver);
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

    private record GenericActorRefFactory(Func<IServiceProvider, IActorRef> Factory) : IEntityActorResolver
    {
        public ValueTask<IActorRef> ResolveAsync(IServiceProvider services, CancellationToken cancellationToken)
            => ValueTask.FromResult(Factory(services));
    }
}