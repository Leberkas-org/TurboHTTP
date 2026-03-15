using System;
using System.Buffers;
using System.Threading;
using System.Threading.Channels;
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

    private ChannelWriter<(IMemoryOwner<byte>, int)>? _outbound;
    private ChannelReader<(IMemoryOwner<byte>, int)>? _inbound;

    private readonly CancellationTokenSource _cts = new();

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private IActorRef? _runner;

    private ISourceQueueWithComplete<DataItem>? _responseQueue;

    public ConnectionActor(TcpOptions options, IActorRef clientManager)
    {
        _options = options;
        _clientManager = clientManager;

        ReceiveAsync<ClientRunner.ClientConnected>(HandleConnected);
        Receive<ClientRunner.ClientDisconnected>(HandleDisconnected);
        Receive<Terminated>(HandleTerminated);
    }

    protected override void PreStart()
    {
        Connect();
    }

    private void Connect()
    {
        _clientManager.Tell(new ClientManager.CreateTcpRunner(_options, Self));
    }

    private async Task HandleConnected(ClientRunner.ClientConnected msg)
    {
        _log.Debug("Connected {0}", msg.RemoteEndPoint);

        _inbound = msg.InboundReader;
        _outbound = msg.OutboundWriter;
        _runner = Sender;

        Context.Watch(_runner);

        var mat = Context.System.Materializer();

        // ---------- RESPONSE STREAM (TCP inbound → SourceRef) ----------
        var responseMat =
            Source.Queue<DataItem>(1024, OverflowStrategy.Backpressure)
                .PreMaterialize(mat);

        _responseQueue = responseMat.Item1;

        var sourceRef = await responseMat.Item2
            .RunWith(StreamRefs.SourceRef<DataItem>(), mat);

        // ---------- REQUEST STREAM (SinkRef → TCP outbound) ----------
        var requestSink = Sink.ForEachAsync<DataItem>(1, async x =>
            await _outbound!.WriteAsync((x.Memory, x.Length)));

        var sinkRef = await requestSink
            .RunWith(StreamRefs.SinkRef<DataItem>(), mat);

        Context.Parent.Tell(new HostPoolActor.RegisterConnectionRefs(Self, sinkRef, sourceRef));

        _ = PumpInbound(_cts.Token);
    }

    private async Task PumpInbound(CancellationToken token)
    {
        try
        {
            await foreach (var item in _inbound!.ReadAllAsync(token))
            {
                if (_responseQueue != null)
                {
                    await _responseQueue.OfferAsync(new DataItem(item.Item1, item.Item2));
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
