using System.Net;
using Servus.Akka.Diagnostics;
using StreamDirection = Servus.Akka.IO.Quic.StreamDirection;

namespace Servus.Akka.IO.Tcp;

public sealed class TcpConnectionFactory : IConnectionFactory
{
    public async Task<ConnectionLease> EstablishAsync(
        ITransportOptions options,
        RequestEndpoint endpoint,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        IClientProvider provider = options switch
        {
            TlsOptions tls => new TlsClientProvider(tls),
            TcpOptions tcp => new TcpClientProvider(tcp),
            _ => throw new ArgumentException($"Unsupported options type: {options.GetType()}", nameof(options))
        };

        var uri = new Uri($"{(options is TlsOptions ? "https" : "http")}://{options.Host}:{options.Port}/");
        var connectActivity = ServusInstrumentation.StartConnect(uri);
        ServusTrace.Connection.Debug(this, "Connecting to {0}:{1}", options.Host, options.Port);

        try
        {
            var stream = await provider.GetStreamAsync(ct).ConfigureAwait(false);

            if (connectActivity is not null && provider.RemoteEndPoint is IPEndPoint remoteEp)
            {
                ServusInstrumentation.SetNetworkPeerAddress(connectActivity, remoteEp.Address.ToString());
            }

            connectActivity?.Stop();
            ServusTrace.Connection.Debug(this, "Connected to {0}:{1}", options.Host, options.Port);

            var state = new ClientState(stream, StreamDirection.Bidirectional);

            var handle = ConnectionHandle.CreateDirect(
                state.OutboundWriter,
                state.InboundReader,
                endpoint);

            var lease = new ConnectionLease(handle, state);

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

            ServusMetrics.OpenConnections.Add(1,
                new("http.connection.state", "active"),
                new("server.address", endpoint.Host),
                new("server.port", endpoint.Port));

            return lease;
        }
        catch (Exception ex)
        {
            ServusTrace.Connection.Warning(this, "Connection to {0}:{1} failed: {2}", options.Host, options.Port,
                ex.Message);

            if (connectActivity is not null)
            {
                ServusInstrumentation.SetError(connectActivity, ex);
                connectActivity.Stop();
            }

            await provider.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}