using System;
using System.Net.Http;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;

namespace TurboHttp.Streams;

public class Http30Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        // TODO: HTTP/3 not yet implemented. This stub provides a valid BidiFlow
        // that accepts input but never produces output, allowing the Engine's
        // 4-port partition graph to materialize without errors.
        return BidiFlow.FromFlows(
            Flow.Create<HttpRequestMessage>()
                .Select(IOutputItem (_) =>
                    throw new NotSupportedException("HTTP/3 is not yet implemented.")),
            Flow.Create<IInputItem>()
                .Select(HttpResponseMessage (_) =>
                    throw new NotSupportedException("HTTP/3 is not yet implemented.")));
    }
}