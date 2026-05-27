using System.Net;
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.Server;

public sealed class TurboEntityTellBuilder
{
    internal Func<TurboHttpContext, Task>? ResponseHandler { get; private set; }

    public void Produces(HttpStatusCode statusCode)
    {
        ResponseHandler = ctx =>
        {
            ctx.Response.StatusCode = (int)statusCode;
            return Task.CompletedTask;
        };
    }

    public void Produces(int statusCode)
    {
        ResponseHandler = ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        };
    }

    public void Handle(Func<TurboHttpContext, Task> writer)
        => ResponseHandler = async ctx => await writer(ctx);

    public void Produces(Func<TurboHttpContext, ITurboResult> factory)
        => ResponseHandler = async ctx => await factory(ctx).ExecuteAsync(ctx);
}