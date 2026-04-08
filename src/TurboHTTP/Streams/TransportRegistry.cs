using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;

namespace TurboHTTP.Streams;

/// <summary>
/// Registry for protocol version-specific transport factories.
/// Maps HTTP versions to <see cref="ITransportFactory"/> instances that create
/// the appropriate transport flow (TCP, QUIC, or custom).
/// </summary>
/// <remarks>
/// <para>
/// <b>Fluent Builder Usage:</b>
/// <code>
/// var transports = new TransportRegistry()
///     .Register(HttpVersion.Version11, tcpFactory)
///     .Register(HttpVersion.Version20, tcpFactory)
///     .Register(HttpVersion.Version30, quicFactory);
/// </code>
/// </para>
/// </remarks>
internal sealed class TransportRegistry
{
    private readonly Dictionary<Version, ITransportFactory> _transports = new();

    /// <summary>
    /// Registers a transport factory for a specific HTTP version.
    /// </summary>
    /// <param name="version">The HTTP version (e.g., <see cref="HttpVersion.Version11"/>, <see cref="HttpVersion.Version20"/>)</param>
    /// <param name="factory">A factory that creates a transport flow for the given version</param>
    /// <returns>This registry instance for fluent chaining</returns>
    public TransportRegistry Register(Version version, ITransportFactory factory)
    {
        _transports[version] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    /// <summary>
    /// Retrieves the transport flow for the given HTTP version by invoking the registered factory.
    /// </summary>
    /// <param name="version">The HTTP version to look up</param>
    /// <returns>A transport flow for the requested version</returns>
    /// <exception cref="InvalidOperationException">Thrown when no factory is registered for the given version</exception>
    public Flow<IOutputItem, IInputItem, NotUsed> Get(Version version)
    {
        if (_transports.TryGetValue(version, out var factory))
        {
            return factory.Create();
        }

        throw new InvalidOperationException(
            $"No transport factory registered for HTTP version {version}. " +
            $"Registered versions: {string.Join(", ", _transports.Keys)}");
    }
}
