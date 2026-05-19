using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace Servus.Akka.Transport.Tcp.Listener;

internal sealed record TcpClientAccepted(TcpClient Client);

internal sealed record TcpAcceptFailed(Exception Error);

internal sealed record TcpConnectionReady(Flow<ITransportOutbound, ITransportInbound, NotUsed> Flow);

internal sealed record TcpConnectionInitFailed(Exception Error);

internal sealed class TcpListenerStage : GraphStage<SourceShape<Flow<ITransportOutbound, ITransportInbound, NotUsed>>>
{
    private readonly TcpListenerOptions _options;

    private readonly Outlet<Flow<ITransportOutbound, ITransportInbound, NotUsed>> _out =
        new("TcpListener.Out");

    public override SourceShape<Flow<ITransportOutbound, ITransportInbound, NotUsed>> Shape { get; }

    public TcpListenerStage(TcpListenerOptions options)
    {
        _options = options;
        Shape = new SourceShape<Flow<ITransportOutbound, ITransportInbound, NotUsed>>(_out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly TcpListenerStage _stage;
        private readonly Queue<Flow<ITransportOutbound, ITransportInbound, NotUsed>> _pendingConnections = new();
        private TcpListener? _listener;
        private IActorRef _self = null!;
        private CancellationTokenSource? _cts;

        public Logic(TcpListenerStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._out, onPull: () => TryPush());
        }

        public override void PreStart()
        {
            var stageActor = GetStageActor(OnReceive);
            _self = stageActor.Ref;
            _cts = new CancellationTokenSource();

            var address = IPAddress.TryParse(_stage._options.Host, out var ip)
                ? ip
                : IPAddress.Any;

            _listener = new TcpListener(address, _stage._options.Port);

            if (_stage._options.ReuseAddress)
            {
                _listener.Server.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true);
            }

            _listener.Start(_stage._options.Backlog);
            _ = AcceptLoopAsync(_listener, _self, _cts.Token);
        }

        public override void PostStop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _listener?.Stop();
            _listener = null;

            while (_pendingConnections.TryDequeue(out _))
            {
            }
        }

        private static async Task AcceptLoopAsync(TcpListener listener, IActorRef self, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    self.Tell(new TcpClientAccepted(client));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    self.Tell(new TcpAcceptFailed(ex));
                    return;
                }
            }
        }

        private void OnReceive((IActorRef sender, object message) args)
        {
            switch (args.message)
            {
                case TcpClientAccepted accepted:
                    OnClientAccepted(accepted.Client);
                    break;
                case TcpAcceptFailed failed:
                    OnAcceptError(failed.Error);
                    break;
                case TcpConnectionReady ready:
                    _pendingConnections.Enqueue(ready.Flow);
                    TryPush();
                    break;
                case TcpConnectionInitFailed failed:
                    Log.Warning(failed.Error, "Failed to initialize accepted connection");
                    break;
            }
        }

        private void OnClientAccepted(TcpClient client)
        {
            _ = InitializeConnectionAsync(client);
        }

        private async Task InitializeConnectionAsync(TcpClient client)
        {
            Stream stream;
            try
            {
                if (_stage._options.NoDelay)
                {
                    client.NoDelay = true;
                }

                if (_stage._options.SocketSendBufferSize is { } sendBuf)
                {
                    client.SendBufferSize = sendBuf;
                }

                if (_stage._options.SocketReceiveBufferSize is { } recvBuf)
                {
                    client.ReceiveBufferSize = recvBuf;
                }

                stream = await GetStreamAsync(client);
            }
            catch (Exception ex)
            {
                client.Dispose();
                _self.Tell(new TcpConnectionInitFailed(ex));
                return;
            }

            var localEndPoint = client.Client.LocalEndPoint!;
            var remoteEndPoint = client.Client.RemoteEndPoint!;

            var connectionInfo = new ConnectionInfo(
                localEndPoint,
                remoteEndPoint,
                TransportProtocol.Tcp);

            var connectionFlow = Flow.FromGraph(
                new TcpServerConnectionStage(stream, connectionInfo));

            _self.Tell(new TcpConnectionReady(connectionFlow));
        }

        private void TryPush()
        {
            if (IsAvailable(_stage._out) && _pendingConnections.TryDequeue(out var flow))
            {
                Push(_stage._out, flow);
            }
        }

        private async Task<Stream> GetStreamAsync(TcpClient client)
        {
            if (_stage._options.ServerCertificate is null)
            {
                return client.GetStream();
            }

            var sslStream = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                _stage._options.ClientCertificateValidationCallback);

            var authOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = _stage._options.ServerCertificate,
                ClientCertificateRequired = _stage._options.ClientCertificateValidationCallback is not null,
                EnabledSslProtocols = _stage._options.EnabledSslProtocols,
                ApplicationProtocols = _stage._options.ApplicationProtocols
            };

            await sslStream.AuthenticateAsServerAsync(authOptions, CancellationToken.None)
                .WaitAsync(_stage._options.HandshakeTimeout, CancellationToken.None);

            return sslStream;
        }

        private void OnAcceptError(Exception ex)
        {
            if (ex is ObjectDisposedException or OperationCanceledException)
            {
                return;
            }

            Log.Error(ex, "TCP listener accept failed");
            FailStage(ex);
        }
    }
}
