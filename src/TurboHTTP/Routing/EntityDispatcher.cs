using Akka.Actor;
using TurboHTTP.Server;
using TurboHTTP.Server.Binding;

namespace TurboHTTP.Routing;

internal sealed class EntityDispatcher : IRouteDispatcher
{
    private readonly EntityMethodConfig _methodConfig;
    private readonly EntityResponseMapperCollection _responseMappers;
    private readonly TimeSpan _timeout;
    private readonly IEntityActorResolver _resolver;

    public EntityDispatcher(
        EntityMethodConfig methodConfig,
        EntityResponseMapperCollection responseMappers,
        TimeSpan timeout,
        IEntityActorResolver resolver)
    {
        _methodConfig = methodConfig;
        _responseMappers = responseMappers;
        _timeout = timeout;
        _resolver = resolver;
    }

    public Task DispatchAsync(TurboHttpContext context, CancellationToken ct)
    {
        return _methodConfig.IsTell
            ? ExecuteTell(context, ct)
            : ExecuteAsk(context, ct);
    }

    private async Task ExecuteAsk(TurboHttpContext ctx, CancellationToken ct)
    {
        try
        {
            var timeout = _methodConfig.TimeoutOverride ?? _timeout;
            var actorRef = await ResolveActor(ctx.RequestServices, ct);
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

    private async Task ExecuteTell(TurboHttpContext ctx, CancellationToken cancellationToken)
    {
        try
        {
            var actorRef = await ResolveActor(ctx.RequestServices, cancellationToken);
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

    private async ValueTask<IActorRef> ResolveActor(IServiceProvider services, CancellationToken ct = default)
    {
        if (_resolver is null)
        {
            throw new InvalidOperationException("No resolver configured for entity actor");
        }

        return await _resolver.ResolveAsync(services, ct);
    }
}