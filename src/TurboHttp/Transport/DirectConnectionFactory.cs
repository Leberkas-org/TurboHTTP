using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using TurboHttp.Diagnostics;
using TurboHttp.Internal;
using TurboHttp.Pooling;

namespace TurboHttp.Transport;

/// <summary>
/// Static factory that establishes a TCP/TLS connection, creates channels,
/// spawns ByteMover tasks, and returns a <see cref="ConnectionLease"/> —
/// all in a single async call with no actor involvement.
/// </summary>
internal static class DirectConnectionFactory
{
    /// <summary>
    /// Establishes a new connection to the specified endpoint and returns a fully
    /// initialised <see cref="ConnectionLease"/> with running ByteMover pump tasks.
    /// </summary>
    /// <param name="options">TCP or TLS connection options.</param>
    /// <param name="endpoint">The target host identity for connection keying.</param>
    /// <param name="ct">Cancellation token for the connection establishment.</param>
    /// <returns>A <see cref="ConnectionLease"/> wrapping the live connection.</returns>
    public static async Task<ConnectionLease> EstablishAsync(
        TcpOptions options,
        RequestEndpoint endpoint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 1. Select provider based on options type
        IClientProvider provider = options switch
        {
            TlsOptions tls => new TlsClientProvider(tls),
            _ => new TcpClientProvider(options)
        };

        try
        {
            // 2. Establish TCP/TLS connection
            var stream = await provider.GetStreamAsync(ct).ConfigureAwait(false);

            // 3. Create ClientState with channels + Pipe
            var state = new ClientState(
                maxFrameSize: options.MaxFrameSize,
                stream: stream,
                inboundChannel: null,
                outboundChannel: null,
                direction: StreamDirection.Bidirectional);

            // 4. Create ConnectionHandle via direct factory (no actor)
            var handle = ConnectionHandle.CreateDirect(
                state.OutboundWriter,
                state.InboundReader,
                endpoint);

            // 5. Create ConnectionLease
            var lease = new ConnectionLease(handle, state);

            // 6. Spawn 3 ByteMover tasks using callback overloads
            //    onClose disposes the lease when any pump exits (error or clean close)
            var closeOnce = 0;
            Action onClose = () =>
            {
                if (Interlocked.CompareExchange(ref closeOnce, 1, 0) == 0)
                {
                    _ = lease.DisposeAsync();
                }
            };

            _ = ClientByteMover.MoveStreamToPipe(state, onClose, log: null, lease.Token);
            _ = ClientByteMover.MovePipeToChannel(state, onClose, log: null, lease.Token);
            _ = ClientByteMover.MoveChannelToStream(state, onClose, log: null, lease.Token);

            // 7. Emit connection opened metrics + diagnostics
            var protocol = VersionToProtocol(endpoint.Version);
            TurboHttpEventSource.Log.ConnectionOpened(endpoint.Host, endpoint.Port, protocol);
            TurboHttpDiagnosticListener.OnConnectionOpened(endpoint.Host, endpoint.Port, protocol);

            return lease;
        }
        catch
        {
            await provider.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static string VersionToProtocol(Version version) => version switch
    {
        { Major: 1, Minor: 0 } => "HTTP/1.0",
        { Major: 1, Minor: 1 } => "HTTP/1.1",
        { Major: 2 } => "HTTP/2",
        { Major: 3 } => "HTTP/3",
        _ => $"HTTP/{version}"
    };
}
