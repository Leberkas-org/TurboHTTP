using Microsoft.AspNetCore.Http;

namespace TurboHTTP.Server;

public sealed class TurboEntityTellBuilder
{
    internal Func<TurboHttpContext, Task>? ResponseHandler { get; private set; }

    public void Response(int statusCode)
    {
        ResponseHandler = ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        };
    }

    public void Response(int statusCode, Func<TurboHttpContext, Task> writer)
        => ResponseHandler = async ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            await writer(ctx);
        };

    public void Produces(Func<TurboHttpContext, IResult> factory)
        => ResponseHandler = async ctx => await factory(ctx).ExecuteAsync(ctx);
}