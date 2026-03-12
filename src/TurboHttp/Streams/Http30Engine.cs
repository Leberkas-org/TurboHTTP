using System;
using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http30Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, ITransportItem, (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        // TODO: HTTP/3 not yet implemented. This stub provides a valid BidiFlow
        // that accepts input but never produces output, allowing the Engine's
        // 4-port partition graph to materialize without errors.
        return BidiFlow.FromFlows(
            Flow.Create<HttpRequestMessage>()
                .Select(ITransportItem (_) =>
                    throw new NotSupportedException("HTTP/3 is not yet implemented.")),
            Flow.Create<(IMemoryOwner<byte>, int)>()
                .Select(HttpResponseMessage (_) =>
                    throw new NotSupportedException("HTTP/3 is not yet implemented.")));
    }
}
