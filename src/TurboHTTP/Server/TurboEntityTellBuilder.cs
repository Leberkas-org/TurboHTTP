using Microsoft.AspNetCore.Http;

namespace TurboHTTP.Server;

public sealed class TurboEntityTellBuilder
{
    internal Func<TurboHttpContext, Task>? ResponseHandler { get; private set; }

    public TurboEntityTellBuilder Response(int statusCode)
    {
        ResponseHandler = ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        };
        return this;
    }

    public TurboEntityTellBuilder Response(int statusCode, Func<TurboHttpContext, Task> writer)
    {
        ResponseHandler = async ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            await writer(ctx);
        };
        return this;
    }

    public TurboEntityTellBuilder Produces(Func<TurboHttpContext, IResult> factory)
    {
        ResponseHandler = async ctx => await factory(ctx).ExecuteAsync(ctx);
        return this;
    }
}
