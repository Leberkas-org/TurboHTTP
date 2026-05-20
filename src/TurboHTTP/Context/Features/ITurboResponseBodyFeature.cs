using Akka.Streams.Dsl;

namespace TurboHTTP.Context.Features;

public interface ITurboResponseBodyFeature
{
    Sink<ReadOnlyMemory<byte>, Task> BodySink { get; }
}