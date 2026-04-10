using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;

namespace TurboHTTP.Streams;

/// <summary>
/// Factory for creating transport stage flows for a specific HTTP version.
/// Abstracts transport creation (TCP, QUIC, or custom) so that <see cref="ProtocolCoreBuilder"/>
/// remains transport-agnostic.
/// </summary>
/// <remarks>
/// Implementations encapsulate transport-specific dependencies (connection manager, options, etc.)
/// and expose a single <see cref="Create"/> method that returns a flow bridging the protocol
/// engine to the wire.
/// </remarks>
internal interface ITransportFactory
{
    /// <summary>
    /// Creates a transport flow connecting protocol output to wire input.
    /// </summary>
    /// <returns>
    /// A flow that consumes <see cref="IOutputItem"/> from the protocol engine and
    /// produces <see cref="IInputItem"/> from the network.
    /// </returns>
    Flow<IOutputItem, IInputItem, NotUsed> Create();
}
