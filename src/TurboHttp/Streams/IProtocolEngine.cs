using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public interface IHttpProtocolEngine
{
    BidiFlow<
        HttpRequestMessage,
        ITransportItem,
        (IMemoryOwner<byte>, int), 
        HttpResponseMessage,
        NotUsed> CreateFlow();
}