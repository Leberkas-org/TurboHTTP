using Akka;
using Akka.Streams.Dsl;

namespace TurboHTTP.Context.Features;

internal sealed class TurboRequestBodyFeature
{
    public Stream Body { get; set; } = Stream.Null;

    public Source<ReadOnlyMemory<byte>, NotUsed> BodySource { get; set; }
        = Source.Empty<ReadOnlyMemory<byte>>();
}
