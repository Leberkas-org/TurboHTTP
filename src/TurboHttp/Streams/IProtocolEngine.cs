using Akka;
using Akka.Streams.Dsl;
using TurboHttp.Internal;

namespace TurboHttp.Streams;

public interface IHttpProtocolEngine
{
    BidiFlow<
        HttpRequestMessage,
        IOutputItem,
        IInputItem, 
        HttpResponseMessage,
        NotUsed> CreateFlow();
}