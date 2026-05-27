using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Sse;
using Servus.Akka.Streams.IO;

namespace Servus.Akka.AspNetCore;

internal sealed class AkkaSseResult(Source<ServerSentEvent, NotUsed> source, IMaterializer materializer) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = 200;
        httpContext.Response.ContentType = "text/event-stream";
        await source
            .Via(SseFormatterFlow.Instance)
            .RunWith(StreamSink.To(httpContext.Response.Body), materializer);
    }
}