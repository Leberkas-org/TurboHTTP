using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class ConnectionActor : ReceiveActor
{
    private readonly TcpOptions _options;
    private readonly IActorRef _clientManager;
    private readonly HostKey _hostKey;

    private System.Threading.Channels.ChannelWriter<(IMemoryOwner<byte>, int)>? _outbound;
    private System.Threading.Channels.ChannelReader<(IMemoryOwner<byte>, int)>? _inbound;

    private readonly CancellationTokenSource _cts = new();

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private IActorRef? _runner;

    private ISourceQueueWithComplete<DataItem>? _responseQueue;

    public ConnectionActor(TcpOptions options, IActorRef clientManager, HostKey hostKey = default)
    {
        _options = options;
        _clientManager = clientManager;
        _hostKey = hostKey;

        Receive<ClientRunner.ClientConnected>(HandleConnected);
        Receive<ClientRunner.ClientDisconnected>(HandleDisconnected);
        Receive<Terminated>(HandleTerminated);
        Receive<DataItem>(HandleOutboundDataItem);
    }

    protected override void PreStart()
    {
        Connect();
    }

    private void Connect()
    {
        _clientManager.Tell(new ClientManager.CreateTcpRunner(_options, Self));
    }

    private void HandleConnected(ClientRunner.ClientConnected msg)
    {
        _log.Debug("Connected {0}", msg.RemoteEndPoint);

        _inbound = msg.InboundReader;
        _outbound = msg.OutboundWriter;
        _runner = Sender;

        Context.Watch(_runner);

        var mat = Context.System.Materializer();

        // ---------- RESPONSE STREAM (TCP inbound → pre-materialized Source) ----------
        var (responseQueue, responseSource) =
            Source.Queue<DataItem>(1024, OverflowStrategy.Backpressure)
                .PreMaterialize(mat);

        _responseQueue = responseQueue;

        // Register with parent — passes response source; HostPoolActor wires the request side
        Context.Parent.Tell(new HostPoolActor.RegisterConnectionRefs(Self, responseSource));

        _ = PumpInbound(_cts.Token);
    }

    private void HandleOutboundDataItem(DataItem item)
    {
        if (_outbound == null)
        {
            return;
        }

        _ = _outbound.WriteAsync((item.Memory, item.Length)).AsTask();
    }

    private async Task PumpInbound(CancellationToken token)
    {
        try
        {
            await foreach (var item in _inbound!.ReadAllAsync(token))
            {
                if (_responseQueue != null)
                {
                    await _responseQueue.OfferAsync(new DataItem(item.Item1, item.Item2) { Key = _hostKey });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Inbound pump failed");
        }
    }

    private void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        _log.Warning("Disconnected {0}", msg.RemoteEndPoint);
        Reconnect();
    }

    private void HandleTerminated(Terminated msg)
    {
        if (msg.ActorRef.Equals(_runner))
        {
            _log.Warning("ClientRunner terminated");
            Reconnect();
        }
    }

    private void Reconnect()
    {
        _responseQueue?.Complete();
        _responseQueue = null;
        _runner = null;
        _outbound = null;
        _inbound = null;

        Connect();
    }

    protected override void PostStop()
    {
        _cts.Cancel();

        try
        {
            _runner?.Tell(new DoClose());
        }
        catch
        {
            // noop
        }

        _responseQueue?.Complete();
    }
}
