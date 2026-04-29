namespace Servus.Akka.Transport.Tcp;

internal sealed class TcpConnectionFactory : ITcpConnectionFactory
{
    public async Task<ConnectionLease> EstablishAsync(TransportOptions options, CancellationToken ct)
    {
        Stream stream;

        if (options is TlsTransportOptions tlsOpts)
        {
            var tlsProvider = new TlsClientProvider(tlsOpts);
            stream = await tlsProvider.GetStreamAsync(ct).ConfigureAwait(false);
        }
        else if (options is TcpTransportOptions tcpOpts)
        {
            var tcpProvider = new TcpClientProvider(tcpOpts);
            stream = await tcpProvider.GetStreamAsync(ct).ConfigureAwait(false);
        }
        else
        {
            throw new ArgumentException($"Unsupported options type: {options.GetType()}", nameof(options));
        }

        var state = new ClientState(stream);
        var cts = new CancellationTokenSource();
        var handle = new ConnectionHandle(state.OutboundWriter, state.InboundReader, cts.Token);
        var lease = new ConnectionLease(handle, state, cts);

        return lease;
    }
}