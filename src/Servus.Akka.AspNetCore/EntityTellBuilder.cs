using System.Net;
using Microsoft.AspNetCore.Http;

namespace Servus.Akka.AspNetCore;

public sealed class EntityTellBuilder
{
    internal Func<HttpContext, Task>? ResponseHandler { get; private set; }

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

    public void Handle(Func<HttpContext, Task> writer)
    {
        ResponseHandler = writer;
    }
}
