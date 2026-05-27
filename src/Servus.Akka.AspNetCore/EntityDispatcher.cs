using Akka.Actor;
using Microsoft.AspNetCore.Http;

namespace Servus.Akka.AspNetCore;

internal sealed class EntityDispatcher(
    EntityMethodConfig methodConfig,
    EntityResponseMapperCollection responseMappers,
    TimeSpan timeout,
    IEntityActorResolver resolver)
{
    internal async Task DispatchAsync(HttpContext ctx, object message)
    {
        if (methodConfig.IsTell)
        {
            await ExecuteTell(ctx, message);
        }
        else
        {
            await ExecuteAsk(ctx, message);
        }
    }

    private async Task ExecuteAsk(HttpContext ctx, object message)
    {
        try
        {
            var askTimeout = methodConfig.TimeoutOverride ?? timeout;
            var actorRef = await resolver.ResolveAsync(ctx.RequestServices, ctx.RequestAborted);
            var response = await actorRef.Ask<object>(message, askTimeout, ctx.RequestAborted);

            var mapper = methodConfig.EndpointMappers?.FindMapper(response.GetType())
                      ?? responseMappers.FindMapper(response.GetType());
            if (mapper is null)
            {
                ctx.Response.StatusCode = 500;
                return;
            }

            await mapper(ctx, response);
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

    private async Task ExecuteTell(HttpContext ctx, object message)
    {
        try
        {
            var actorRef = await resolver.ResolveAsync(ctx.RequestServices, ctx.RequestAborted);
            actorRef.Tell(message);

            if (methodConfig.TellResponseHandler is not null)
            {
                await methodConfig.TellResponseHandler(ctx);
            }
            else
            {
                ctx.Response.StatusCode = 202;
            }
        }
        catch
        {
            ctx.Response.StatusCode = 503;
        }
    }
}
