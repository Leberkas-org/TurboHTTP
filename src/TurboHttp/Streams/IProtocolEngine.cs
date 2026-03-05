using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams.Dsl;

namespace TurboHttp.Streams;

public interface IHttpProtocolEngine
{
    BidiFlow<
        HttpRequestMessage,
        (IMemoryOwner<byte>, int),
        (IMemoryOwner<byte>, int), 
        HttpResponseMessage,
        NotUsed> CreateFlow();
}