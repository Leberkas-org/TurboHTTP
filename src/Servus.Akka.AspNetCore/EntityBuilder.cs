using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Servus.Akka.AspNetCore;

public sealed class EntityBuilder
{
    private readonly Dictionary<string, EntityMethodBuilder> _methods = new(StringComparer.OrdinalIgnoreCase);
    private readonly EntityResponseMapperCollection _responseMappers = new();
    private TimeSpan _timeout = TimeSpan.FromSeconds(5);
    private IEntityActorResolver _resolver = new ServiceProviderActorResolver(_ => ActorRefs.Nobody);

    internal IReadOnlyDictionary<string, EntityMethodBuilder> Methods => _methods;
    internal EntityResponseMapperCollection ResponseMappers => _responseMappers;
    internal TimeSpan Timeout => _timeout;
    internal IEntityActorResolver Resolver => _resolver;

    public EntityMethodBuilder OnGet(Delegate messageFactory)
        => AddMethod("GET", messageFactory);

    public EntityMethodBuilder OnPost(Delegate messageFactory)
        => AddMethod("POST", messageFactory);

    public EntityMethodBuilder OnPut(Delegate messageFactory)
        => AddMethod("PUT", messageFactory);

    public EntityMethodBuilder OnDelete(Delegate messageFactory)
        => AddMethod("DELETE", messageFactory);

    public EntityMethodBuilder OnPatch(Delegate messageFactory)
        => AddMethod("PATCH", messageFactory);

    public EntityBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    public EntityBuilder UseResolver(IEntityActorResolver resolver)
    {
        _resolver = resolver;
        return this;
    }

    public EntityBuilder UseActorRef<TActorKey>()
        => UseActorRef(registry => registry.Get<TActorKey>());

    public EntityBuilder UseActorRef(Func<IReadOnlyActorRegistry, IActorRef> factory)
        => UseResolver(new ServiceProviderActorResolver(
            sp => factory(sp.GetRequiredService<IReadOnlyActorRegistry>())));

    public EntityBuilder Response<TResponse>(Func<HttpContext, TResponse, Task> mapper)
    {
        _responseMappers.Add(mapper);
        return this;
    }

    private EntityMethodBuilder AddMethod(string method, Delegate messageFactory)
    {
        var builder = new EntityMethodBuilder(messageFactory);
        _methods[method] = builder;
        return builder;
    }

    internal sealed record ServiceProviderActorResolver(
        Func<IServiceProvider, IActorRef> Factory) : IEntityActorResolver
    {
        public ValueTask<IActorRef> ResolveAsync(IServiceProvider services, CancellationToken cancellationToken)
            => ValueTask.FromResult(Factory(services));
    }
}
