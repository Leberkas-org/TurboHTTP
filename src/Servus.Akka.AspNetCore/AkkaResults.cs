using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Sse;

namespace Servus.Akka.AspNetCore;

public static class AkkaResults
{
    public static IResult Stream(Source<ReadOnlyMemory<byte>, NotUsed> source, IMaterializer materializer,
        string contentType = "application/octet-stream")
    {
        return new AkkaStreamResult(source, materializer, contentType);
    }

    public static IResult ServerSentEvent(Source<ServerSentEvent, NotUsed> source, IMaterializer materializer)
    {
        return new AkkaSseResult(source, materializer);
    }
}