using Akka.Actor;
using TurboHTTP.Server;
using TurboHTTP.Server.Binding;

namespace TurboHTTP.Routing;

internal sealed class EntityDispatcher : IRouteDispatcher
{
    private readonly string _entityKeyParam;
    private readonly EntityMethodConfig _methodConfig;
    private readonly EntityResponseMapperCollection _responseMappers;
    private readonly TimeSpan _timeout;
    private readonly Func<IServiceProvider, IEntityActorResolver>? _resolverFactory;

    public EntityDispatcher(
        string entityKeyParam,
        EntityMethodConfig methodConfig,
        EntityResponseMapperCollection responseMappers,
        TimeSpan timeout,
        Func<IServiceProvider, IEntityActorResolver>? resolverFactory)
    {
        _entityKeyParam = entityKeyParam;
        _methodConfig = methodConfig;
        _responseMappers = responseMappers;
        _timeout = timeout;
        _resolverFactory = resolverFactory;
    }

    public Task DispatchAsync(TurboHttpContext context, CancellationToken ct)
    {
        var entityKey = context.Request.RouteValues[_entityKeyParam]?.ToString() ?? string.Empty;

        return _methodConfig.IsTell
            ? ExecuteTell(context, entityKey)
            : ExecuteAsk(context, entityKey, ct);
    }

    private async Task ExecuteAsk(TurboHttpContext ctx, string entityKey, CancellationToken ct)
    {
        try
        {
            var timeout = _methodConfig.TimeoutOverride ?? _timeout;
            var actorRef = await ResolveActor(entityKey, ctx.RequestServices);
            var message = await _methodConfig.MessageFactory(ctx, ctx.RequestServices);
            var response = await actorRef.Ask<object>(message, timeout, ct);

            var mapper = _responseMappers.FindMapper(response.GetType());
            if (mapper is null)
            {
                ctx.Response.StatusCode = 500;
                return;
            }

            await mapper(ctx, response);
        }
        catch (BindingValidationException ex)
        {
            ctx.Response.StatusCode = ex.StatusCode;
            if (ex.Errors.Count > 0)
            {
                await ParameterValidator.WriteValidationError(ctx, ex.Errors);
            }
        }
        catch (TaskCanceledException)
        {
            ctx.Response.StatusCode = 504;
        }
        catch (AskTimeoutException)
        {
            ctx.Response.StatusCode = 504;
        }
        catch
        {
            ctx.Response.StatusCode = 500;
        }
    }

    private async Task ExecuteTell(TurboHttpContext ctx, string entityKey)
    {
        try
        {
            var actorRef = await ResolveActor(entityKey, ctx.RequestServices);
            var message = await _methodConfig.MessageFactory(ctx, ctx.RequestServices);
            actorRef.Tell(message);
            ctx.Response.StatusCode = 202;
        }
        catch (BindingValidationException ex)
        {
            ctx.Response.StatusCode = ex.StatusCode;
            if (ex.Errors.Count > 0)
            {
                await ParameterValidator.WriteValidationError(ctx, ex.Errors);
            }
        }
        catch
        {
            ctx.Response.StatusCode = 503;
        }
    }

    private async ValueTask<IActorRef> ResolveActor(string entityKey, IServiceProvider services)
    {
        if (_resolverFactory is null)
        {
            throw new InvalidOperationException("No resolver configured for entity actor");
        }

        var resolver = _resolverFactory(services);
        return await resolver.ResolveAsync(entityKey, services, CancellationToken.None);
    }
}