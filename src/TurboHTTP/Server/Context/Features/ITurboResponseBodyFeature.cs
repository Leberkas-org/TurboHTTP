using Akka.Streams.Dsl;

namespace TurboHTTP.Server.Context.Features;

public interface ITurboResponseBodyFeature
{
    Sink<ReadOnlyMemory<byte>, Task> BodySink { get; }
}