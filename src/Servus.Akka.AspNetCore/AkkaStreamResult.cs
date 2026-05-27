using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;

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
        var body = httpContext.Response.Body;
        await source.RunForeach(
            async chunk => await body.WriteAsync(chunk),
            materializer);
    }
}
