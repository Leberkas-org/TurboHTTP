using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Server;

public static class TurboStreamResults
{
    public static IResult EventStream(Source<string, NotUsed> source)
    {
        return new EventStreamResult(source);
    }

    public static IResult Stream(Source<ReadOnlyMemory<byte>, NotUsed> source, string? contentType = null)
    {
        return new AkkaStreamResult(source, contentType);
    }
}

internal sealed class EventStreamResult(Source<string, NotUsed> source) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = 200;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers["Cache-Control"] = "no-cache";

        if (httpContext is not TurboHttpContext turboCtx)
        {
            return;
        }

        if (httpContext.Features.Get<ITurboResponseBodyFeature>() is not TurboHttpResponseBodyFeature bodyFeature)
        {
            return;
        }

        var byteSource = source.Select(text =>
        {
            var formatted = string.Concat("data: ", text, "\n\n");
            return (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(formatted).AsMemory();
        });

        await byteSource.RunWith(bodyFeature.BodySink, turboCtx.Materializer);
        await bodyFeature.CompleteAsync();
    }
}

internal sealed class AkkaStreamResult(Source<ReadOnlyMemory<byte>, NotUsed> source, string? contentType) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = 200;
        if (contentType is not null)
        {
            httpContext.Response.ContentType = contentType;
        }

        if (httpContext is not TurboHttpContext turboCtx)
        {
            return;
        }

        if (httpContext.Features.Get<ITurboResponseBodyFeature>() is not TurboHttpResponseBodyFeature bodyFeature)
        {
            return;
        }

        await source.RunWith(bodyFeature.BodySink, turboCtx.Materializer);
        await bodyFeature.CompleteAsync();
    }
}