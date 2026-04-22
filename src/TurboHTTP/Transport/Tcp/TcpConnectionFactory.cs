using System.Net;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Quic;

namespace TurboHTTP.Transport.Tcp;

/// <summary>
/// Static factory that establishes a TCP/TLS connection, creates channels,
/// spawns ByteMover tasks, and returns a <see cref="ConnectionLease"/> —
/// all in a single async call with no actor involvement.
/// </summary>
internal sealed class TcpConnectionFactory : IConnectionFactory
{
    public static readonly TcpConnectionFactory Instance = new();

    Task<ConnectionLease> IConnectionFactory.EstablishAsync(ITransportOptions options, RequestEndpoint endpoint,
        CancellationToken ct)
        => EstablishAsync(options, endpoint, ct);

    /// <summary>
    /// Establishes a new connection to the specified endpoint and returns a fully
    /// initialised <see cref="ConnectionLease"/> with running ByteMover pump tasks.
    /// </summary>
    /// <param name="options">TCP or TLS connection options.</param>
    /// <param name="endpoint">The target host identity for connection keying.</param>
    /// <param name="ct">Cancellation token for the connection establishment.</param>
    /// <returns>A <see cref="ConnectionLease"/> wrapping the live connection.</returns>
    public static async Task<ConnectionLease> EstablishAsync(
        ITransportOptions options,
        RequestEndpoint endpoint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 1. Select provider based on options type
        IClientProvider provider = options switch
        {
            TlsOptions tls => new TlsClientProvider(tls),
            TcpOptions tcp => new TcpClientProvider(tcp),
            _ => throw new ArgumentException($"Unsupported options type: {options.GetType()}", nameof(options))
        };

        // Start a Connect span that wraps the entire establishment (DNS + socket + TLS)
        var uri = new Uri($"{(options is TlsOptions ? "https" : "http")}://{endpoint.Host}:{endpoint.Port}/");
        var connectActivity = TurboHttpInstrumentation.StartConnect(uri);
        TurboHttpEventSource.Instance.ConnectionStart(endpoint.Host, endpoint.Port);

        try
        {
            // 2. Establish TCP/TLS connection
            var stream = await provider.GetStreamAsync(ct).ConfigureAwait(false);

            // Set resolved peer address on the Connect span
            if (connectActivity is not null && provider.RemoteEndPoint is IPEndPoint remoteEp)
            {
                TurboHttpInstrumentation.SetNetworkPeerAddress(connectActivity, remoteEp.Address.ToString());
            }

            connectActivity?.Stop();

            // 3. Create ClientState with channels + Pipe
            var state = new ClientState(
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
            var onClose = () =>
            {
                if (Interlocked.CompareExchange(ref closeOnce, 1, 0) == 0)
                {
                    lease.Dispose();
                }
            };

            _ = ClientByteMover.MoveStreamToChannel(state, onClose, lease.Token);
            _ = ClientByteMover.MoveChannelToStream(state, onClose, lease.Token);

            // 7. Emit connection opened metrics + diagnostics
            var protocol = VersionToProtocol(endpoint.Version);
            TurboHttpMetrics.OpenConnections.Add(1,
                new("http.connection.state", "active"),
                new("server.address", endpoint.Host),
                new("server.port", endpoint.Port));
            TurboTrace.Connection.Info(typeof(TcpConnectionFactory), "Connection opened: {0}:{1} ({2})",
                endpoint.Host, endpoint.Port, protocol);

            return lease;
        }
        catch (Exception ex)
        {
            if (connectActivity is not null)
            {
                TurboHttpInstrumentation.SetError(connectActivity, ex);
                connectActivity.Stop();
            }

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