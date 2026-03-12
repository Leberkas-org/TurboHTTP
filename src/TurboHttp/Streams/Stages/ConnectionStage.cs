using System.Buffers;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.IO;

namespace TurboHttp.Streams.Stages;

public interface ITransportItem
{
    bool IsTls { get; }
}

public record ConnectItem(TcpOptions Options) : ITransportItem
{
    public bool IsTls => Options is TlsOptions;
}

public record DataItem(IMemoryOwner<byte> Memory, int Length, bool IsTls = false) : ITransportItem;

public sealed class ConnectionStage : GraphStage<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>>
{
    private IActorRef ClientManager { get; }

    private readonly Inlet<ITransportItem> _inlet = new("tcp.in");
    private readonly Outlet<(IMemoryOwner<byte>, int)> _outlet = new("tcp.out");

    public override FlowShape<ITransportItem, (IMemoryOwner<byte>, int)> Shape { get; }

    public ConnectionStage(IActorRef clientManager)
    {
        ClientManager = clientManager;
        Shape = new FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionStage _stage;
        private IActorRef _self = ActorRefs.Nobody;

        private bool _connected;
        private bool _stopping;
        private TcpOptions? _options;
        private IActorRef _runner = ActorRefs.Nobody;

        private ChannelReader<(IMemoryOwner<byte>, int)>? _inboundReader;
        private ChannelWriter<(IMemoryOwner<byte>, int)>? _outboundWriter;

        private readonly Queue<(IMemoryOwner<byte>, int)> _pendingWrites = new();
        private readonly Queue<(IMemoryOwner<byte>, int)> _pendingReads = new();

        public Logic(ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet, onPush: HandlePush, onUpstreamFinish: () =>
            {
                _stopping = true;
                CompleteStage();
            });
            SetHandler(stage._outlet, onPull: () =>
            {
                if (_pendingReads.TryDequeue(out var chunk))
                {
                    Push(_stage._outlet, chunk);
                }
            }, onDownstreamFinish: _ =>
            {
                _stopping = true;
                CompleteStage();
            });
        }

        public override void PreStart()
        {
            _self = GetStageActor(OnMessage).Ref;
            Pull(_stage._inlet);
        }

        private void HandlePush()
        {
            var elem = Grab(_stage._inlet);

            switch (elem)
            {
                case ConnectItem init:
                    _options ??= init.Options;
                    Pull(_stage._inlet);
                    break;

                case DataItem data:
                    if (_connected)
                    {
                        _outboundWriter?.TryWrite((data.Memory, data.Length));
                        Pull(_stage._inlet);
                    }
                    else
                    {
                        _pendingWrites.Enqueue((data.Memory, data.Length));
                        TryConnect();
                    }

                    break;
            }
        }

        private void TryConnect()
        {
            if (_stopping || _options == null) return;
            _stage.ClientManager.Tell(new ClientManager.CreateTcpRunner(_options, _self));
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            switch (args.msg)
            {
                case ClientRunner.ClientConnected c:
                    if (_stopping)
                    {
                        // Stage is shutting down — close the runner immediately
                        args.sender.Tell(DoClose.Instance);
                        return;
                    }

                    _connected = true;
                    _runner = args.sender;
                    _inboundReader = c.InboundReader;
                    _outboundWriter = c.OutboundWriter;
                    _ = BeginReadLoop(_inboundReader);
                    while (_pendingWrites.TryDequeue(out var cw))
                    {
                        _outboundWriter.TryWrite(cw);
                    }

                    if (!HasBeenPulled(_stage._inlet))
                    {
                        Pull(_stage._inlet);
                    }

                    break;

                case ClientRunner.ClientDisconnected:
                    _connected = false;
                    _runner = ActorRefs.Nobody;
                    _inboundReader = null;
                    _outboundWriter = null;
                    break;
            }
        }

        private async Task BeginReadLoop(ChannelReader<(IMemoryOwner<byte>, int)> reader)
        {
            var pushCallback = GetAsyncCallback<(IMemoryOwner<byte>, int)>(chunk =>
            {
                if (IsAvailable(_stage._outlet))
                {
                    Push(_stage._outlet, chunk);
                }
                else
                {
                    _pendingReads.Enqueue(chunk);
                }
            });

            await foreach (var chunk in reader.ReadAllAsync())
            {
                pushCallback(chunk);
            }

            var disconnected = GetAsyncCallback(() =>
            {
                _connected = false;
                TryConnect();
            });
            disconnected();
        }

        public override void PostStop()
        {
            _stopping = true;
            _outboundWriter?.TryComplete();

            if (!_runner.IsNobody())
            {
                _runner.Tell(DoClose.Instance);
            }

            while (_pendingWrites.TryDequeue(out var chunk))
            {
                chunk.Item1.Dispose();
            }

            while (_pendingReads.TryDequeue(out var chunk))
            {
                chunk.Item1.Dispose();
            }
        }
    }
}