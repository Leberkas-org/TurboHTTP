using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Servus.Akka.AspNetCore;

public static class EntityEndpointExtensions
{
    public static RouteHandlerBuilder MapEntity(
        this IEndpointRouteBuilder builder,
        string pattern,
        Action<EntityBuilder> configure)
    {
        var entityBuilder = new EntityBuilder();
        configure(entityBuilder);
        return RegisterEndpoints(builder, pattern, entityBuilder);
    }

    public static RouteHandlerBuilder MapEntity<TActorKey>(
        this IEndpointRouteBuilder builder,
        string pattern,
        Action<EntityBuilder> configure)
    {
        var entityBuilder = new EntityBuilder();
        entityBuilder.UseActorRef<TActorKey>();
        configure(entityBuilder);
        return RegisterEndpoints(builder, pattern, entityBuilder);
    }

    private static RouteHandlerBuilder RegisterEndpoints(
        IEndpointRouteBuilder builder,
        string pattern,
        EntityBuilder entityBuilder)
    {
        RouteHandlerBuilder? lastBuilder = null;

        foreach (var (method, methodBuilder) in entityBuilder.Methods)
        {
            var config = methodBuilder.ToConfig();
            var dispatcher = new EntityDispatcher(
                config,
                entityBuilder.ResponseMappers,
                entityBuilder.Timeout,
                entityBuilder.Resolver);

            var compositeDelegate = EntityDelegateComposer.Compose(
                config.MessageFactory, dispatcher);

            lastBuilder = builder.MapMethods(pattern, [method], compositeDelegate);
        }

        if (lastBuilder is null)
        {
            throw new InvalidOperationException(
                "MapEntity requires at least one HTTP method (OnGet, OnPost, etc.).");
        }

        return lastBuilder;
    }
}
