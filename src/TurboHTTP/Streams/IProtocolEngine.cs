using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.IO;

namespace TurboHTTP.Streams;

internal interface IHttpProtocolEngine
{
    BidiFlow<
        HttpRequestMessage,
        IOutputItem,
        IInputItem,
        HttpResponseMessage,
        NotUsed> CreateFlow();
}