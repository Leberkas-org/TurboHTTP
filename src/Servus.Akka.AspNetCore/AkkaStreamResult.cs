using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Streams.IO;

namespace Servus.Akka.AspNetCore;

internal sealed class AkkaStreamResult(
    Source<ReadOnlyMemory<byte>, NotUsed> source,
    IMaterializer materializer,
    string contentType) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = 200;
        httpContext.Response.ContentType = contentType;
        await source.RunWith(StreamSink.To(httpContext.Response.Body), materializer);
    }
}