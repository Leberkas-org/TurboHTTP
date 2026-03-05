using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.IO;

namespace TurboHttp.Streams.Stages;

public sealed class ConnectionStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>>
{
    internal IActorRef ClientManager { get; }
    internal TcpOptions Options { get; }

    private readonly Inlet<(IMemoryOwner<byte>, int)> _inlet = new("tcp.in");
    private readonly Outlet<(IMemoryOwner<byte>, int)> _outlet = new("tcp.out");

    public override FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)> Shape { get; }

    public ConnectionStage(IActorRef clientManager, TcpOptions options)
    {
        ClientManager = clientManager;
        Options = options;
        Shape = new FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionStage _stage;
        private StageActor? _self;
        private bool _connected;
        private bool _downstreamWaiting;
        private int _reconnectAttempts;
        private readonly Queue<(IMemoryOwner<byte>, int)> _outboundBuffer = new();
        private ChannelWriter<(IMemoryOwner<byte>, int)>? _outboundWriter;
        private ChannelReader<(IMemoryOwner<byte>, int)>? _inboundReader;

        private Action? _onReadReady;
        private Action? _onDisconnected;

        public Logic(ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var chunk = Grab(stage._inlet);
                    if (_connected)
                    {
                        _outboundWriter!.TryWrite(chunk);
                    }
                    else
                    {
                        _outboundBuffer.Enqueue(chunk);
                        _stage.ClientManager.Tell(new ClientManager.CreateTcpRunner(_stage.Options, _self!.Ref));
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet,
                onPull: () =>
                {
                    _downstreamWaiting = true;
                    if (_connected)
                    {
                        TryReadInbound();
                    }
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart()
        {
            _onReadReady = GetAsyncCallback(TryReadInbound);
            _onDisconnected = GetAsyncCallback(HandleDisconnected);

            _self = GetStageActor(OnMessage);
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            switch (args.msg)
            {
                case ClientRunner.ClientConnected connected:
                    _connected = true;
                    _reconnectAttempts = 0;
                    _inboundReader = connected.InboundReader;
                    _outboundWriter = connected.OutboundWriter;

                    while (_outboundBuffer.TryDequeue(out var chunk))
                    {
                        _outboundWriter.TryWrite(chunk);
                    }

                    Pull(_stage._inlet);

                    if (_downstreamWaiting)
                    {
                        TryReadInbound();
                    }

                    break;

                case ClientRunner.ClientDisconnected:
                    HandleDisconnected();
                    break;
            }
        }

        private void TryReadInbound()
        {
            if (_inboundReader is null)
            {
                return;
            }

            if (_inboundReader.TryRead(out var chunk))
            {
                _downstreamWaiting = false;
                Push(_stage._outlet, chunk);
                return;
            }

            _inboundReader
                .WaitToReadAsync()
                .AsTask()
                .ContinueWith(t =>
                {
                    if (t is { IsCompletedSuccessfully: true, Result: true })
                    {
                        _onReadReady!();
                    }
                    else
                    {
                        _onDisconnected!();
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private void HandleDisconnected()
        {
            _connected = false;
            _inboundReader = null;
            _outboundWriter = null;

            var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, _reconnectAttempts++)));
            var stageRef = _self.Ref;
            var options = _stage.Options;
            var manager = _stage.ClientManager;

            Task.Delay(delay).ContinueWith(_ =>
                manager.Tell(new ClientManager.CreateTcpRunner(options, stageRef)));
        }

        public override void PostStop()
        {
            while (_outboundBuffer.TryDequeue(out var chunk))
            {
                chunk.Item1.Dispose();
            }
        }
    }
}

public interface IConnectionItem;

public record InitialInput(TcpOptions Options) : IConnectionItem;

public record DataInput(IMemoryOwner<byte> Memory, int Length) : IConnectionItem;

public sealed class ConnectionV2Stage : GraphStage<FlowShape<IConnectionItem, (IMemoryOwner<byte>, int)>>
{
    internal IActorRef ClientManager { get; }

    private readonly Inlet<IConnectionItem> _inlet = new("tcp.in");
    private readonly Outlet<(IMemoryOwner<byte>, int)> _outlet = new("tcp.out");

    public override FlowShape<IConnectionItem, (IMemoryOwner<byte>, int)> Shape { get; }

    public ConnectionV2Stage(IActorRef clientManager)
    {
        ClientManager = clientManager;
        Shape = new FlowShape<IConnectionItem, (IMemoryOwner<byte>, int)>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionV2Stage _stage;
        private StageActor? _self;
        private bool _connected;
        private bool _downstreamWaiting;
        private int _reconnectAttempts;
        private TcpOptions? _options;
        private readonly Queue<(IMemoryOwner<byte>, int)> _outboundBuffer = new();
        private ChannelWriter<(IMemoryOwner<byte>, int)>? _outboundWriter;
        private ChannelReader<(IMemoryOwner<byte>, int)>? _inboundReader;

        private Action? _onReadReady;
        private Action? _onDisconnected;

        public Logic(ConnectionV2Stage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var input = Grab(stage._inlet);

                    if (input is InitialInput initial && _options is null)
                    {
                        _options = initial.Options;
                        Pull(_stage._inlet); 
                        return;
                    }

                    (IMemoryOwner<byte> memory, int length) chunk = input switch
                    {
                        DataInput d => (d.Memory, d.Length),
                        _ => throw new InvalidOperationException("Unknown input type")
                    };

                    if (_connected)
                    {
                        _outboundWriter!.TryWrite(chunk);
                    }
                    else
                    {
                        _outboundBuffer.Enqueue(chunk);

                        if (_options is not null)
                        {
                            _stage.ClientManager.Tell(new ClientManager.CreateTcpRunner(_options, _self!.Ref));
                        }
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet,
                onPull: () =>
                {
                    _downstreamWaiting = true;
                    if (_connected)
                    {
                        TryReadInbound();
                    }
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart()
        {
            _onReadReady = GetAsyncCallback(TryReadInbound);
            _onDisconnected = GetAsyncCallback(HandleDisconnected);
            _self = GetStageActor(OnMessage);
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            switch (args.msg)
            {
                case ClientRunner.ClientConnected connected:
                    _connected = true;
                    _reconnectAttempts = 0;
                    _inboundReader = connected.InboundReader;
                    _outboundWriter = connected.OutboundWriter;

                    while (_outboundBuffer.TryDequeue(out var chunk))
                    {
                        _outboundWriter.TryWrite(chunk);
                    }

                    Pull(_stage._inlet);

                    if (_downstreamWaiting)
                    {
                        TryReadInbound();
                    }

                    break;

                case ClientRunner.ClientDisconnected:
                    HandleDisconnected();
                    break;
            }
        }

        private void TryReadInbound()
        {
            if (_inboundReader is null) return;

            if (_inboundReader.TryRead(out var chunk))
            {
                _downstreamWaiting = false;
                Push(_stage._outlet, chunk);
                return;
            }

            _inboundReader
                .WaitToReadAsync()
                .AsTask()
                .ContinueWith(t =>
                {
                    if (t is { IsCompletedSuccessfully: true, Result: true })
                    {
                        _onReadReady!();
                    }
                    else
                    {
                        _onDisconnected!();
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private void HandleDisconnected()
        {
            _connected = false;
            _inboundReader = null;
            _outboundWriter = null;

            if (_options is null) return;

            var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, _reconnectAttempts++)));
            var stageRef = _self!.Ref;
            var options = _options;
            var manager = _stage.ClientManager;

            Task.Delay(delay).ContinueWith(_ =>
                manager.Tell(new ClientManager.CreateTcpRunner(options, stageRef)));
        }

        public override void PostStop()
        {
            while (_outboundBuffer.TryDequeue(out var chunk))
            {
                chunk.Item1.Dispose();
            }
        }
    }
}