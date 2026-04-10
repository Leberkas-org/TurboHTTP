using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;

#pragma warning disable CA1416

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Transport factory for QUIC connections (HTTP/3).
/// QUIC is self-contained and requires no external dependencies (no connection manager, no client options).
/// </summary>
internal sealed class QuicTransportFactory : ITransportFactory
{
    /// <summary>
    /// Creates a QUIC transport stage.
    /// </summary>
    /// <returns>A flow wrapping a <see cref="QuicConnectionStage"/>.</returns>
    public Flow<IOutputItem, IInputItem, NotUsed> Create()
    {
        return Flow.FromGraph(new QuicConnectionStage());
    }
}
