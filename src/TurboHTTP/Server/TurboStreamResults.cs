using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using TurboHTTP.Context.Features;
using TurboHTTP.Features.Sse;

namespace TurboHTTP.Server;

public static class TurboStreamResults
{
    public static ITurboResult EventStream(Source<string, NotUsed> source)
    {
        return new EventStreamResult(source);
    }

    public static ITurboResult EventStream(Source<ServerSentEvent, NotUsed> source)
    {
        return new SseEventStreamResult(source);
    }

    public static ITurboResult Stream(Source<ReadOnlyMemory<byte>, NotUsed> source, string? contentType = null)
    {
        return new AkkaStreamResult(source, contentType);
    }
}

internal sealed class EventStreamResult(Source<string, NotUsed> source) : ITurboResult
{
    public async Task ExecuteAsync(TurboHttpContext turboCtx)
    {

        turboCtx.Response.StatusCode = 200;
        turboCtx.Response.ContentType = "text/event-stream";
        turboCtx.Response.Headers.CacheControl = "no-cache";

        if (turboCtx.Features.Get<ITurboResponseBodyFeature>() is not TurboHttpResponseBodyFeature bodyFeature)
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

internal sealed class SseEventStreamResult(Source<ServerSentEvent, NotUsed> source) : ITurboResult
{
    public async Task ExecuteAsync(TurboHttpContext turboCtx)
    {

        turboCtx.Response.StatusCode = 200;
        turboCtx.Response.ContentType = "text/event-stream";
        turboCtx.Response.Headers.CacheControl = "no-cache";

        if (turboCtx.Features.Get<ITurboResponseBodyFeature>() is not TurboHttpResponseBodyFeature bodyFeature)
        {
            return;
        }

        var byteSource = source.Via(SseFormatterFlow.Instance);

        await byteSource.RunWith(bodyFeature.BodySink, turboCtx.Materializer);
        await bodyFeature.CompleteAsync();
    }
}

internal sealed class AkkaStreamResult(Source<ReadOnlyMemory<byte>, NotUsed> source, string? contentType) : ITurboResult
{
    public async Task ExecuteAsync(TurboHttpContext turboCtx)
    {

        turboCtx.Response.StatusCode = 200;
        if (contentType is not null)
        {
            turboCtx.Response.ContentType = contentType;
        }

        if (turboCtx.Features.Get<ITurboResponseBodyFeature>() is not TurboHttpResponseBodyFeature bodyFeature)
        {
            return;
        }

        await source.RunWith(bodyFeature.BodySink, turboCtx.Materializer);
        await bodyFeature.CompleteAsync();
    }
}