using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;

namespace TurboHTTP.Streams;

public interface IHttpProtocolEngine
{
    BidiFlow<
        HttpRequestMessage,
        IOutputItem,
        IInputItem, 
        HttpResponseMessage,
        NotUsed> CreateFlow();
}