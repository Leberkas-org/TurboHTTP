using Akka;
using Akka.Streams.Dsl;

namespace TurboHTTP.Server.Context.Features;

public interface ITurboRequestBodyFeature
{
    Source<ReadOnlyMemory<byte>, NotUsed> BodySource { get; }
}