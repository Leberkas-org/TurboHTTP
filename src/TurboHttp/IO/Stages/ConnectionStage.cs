using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.IO.Stages;

public sealed class ConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private IActorRef PoolRouter { get; }

    private readonly Inlet<IOutputItem> _inlet = new("connection.in");
    private readonly Outlet<IInputItem> _outlet = new("connection.out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public ConnectionStage(IActorRef poolRouter)
    {
        PoolRouter = poolRouter;
        Shape = new FlowShape<IOutputItem, IInputItem>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionStage _stage;
        private readonly Queue<IInputItem> _pendingReads = new();

        /// <summary>Current connection handle providing direct channel I/O.</summary>
        private ConnectionHandle? _handle;

        /// <summary>Callback bridging async channel reads into the stage event loop.</summary>
        private Action<IInputItem>? _onInboundData;

        /// <summary>Callback bridging async channel write completion into the stage event loop.</summary>
        private Action? _onOutboundWriteDone;

        /// <summary>Callback invoked when a <see cref="ConnectionHandle"/> is received from the actor hierarchy.</summary>
        private Action<ConnectionHandle>? _onHandleReceived;

        /// <summary>Callback invoked when the inbound channel completes (connection closed).</summary>
        private Action? _onInboundComplete;

        private StageActor? _stageActor;
        private CancellationTokenSource? _pumpCts;

        /// <summary>The HostKey from the most recent ConnectItem — used to tag inbound DataItems.</summary>
        private HostKey _currentKey;

        public Logic(ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    StopInboundPump();
                    CompleteStage();
                });

            SetHandler(stage._outlet,
                onPull: () =>
                {
                    if (_pendingReads.TryDequeue(out var item))
                    {
                        Push(_stage._outlet, item);
                    }
                },
                onDownstreamFinish: _ =>
                {
                    StopInboundPump();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            _onInboundData = GetAsyncCallback<IInputItem>(item =>
            {
                if (IsAvailable(_stage._outlet))
                {
                    Push(_stage._outlet, item);
                }
                else
                {
                    _pendingReads.Enqueue(item);
                }
            });

            _onOutboundWriteDone = GetAsyncCallback(() =>
            {
                if (!IsClosed(_stage._inlet) && !HasBeenPulled(_stage._inlet))
                {
                    Pull(_stage._inlet);
                }
            });

            _onHandleReceived = GetAsyncCallback<ConnectionHandle>(handle =>
            {
                _handle = handle;
                _currentKey = handle.Key;
                StartInboundPump();

                // Now ready to process data items — pull next element.
                if (!IsClosed(_stage._inlet) && !HasBeenPulled(_stage._inlet))
                {
                    Pull(_stage._inlet);
                }
            });

            _onInboundComplete = GetAsyncCallback(() =>
            {
                // Connection closed — clear the handle so next ConnectItem re-acquires.
                _handle = null;
            });

            _stageActor = GetStageActor(OnMessage);

            // Ready to accept ConnectItem immediately — no GlobalRefs needed.
            Pull(_stage._inlet);
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            if (args.msg is ConnectionHandle handle)
            {
                _onHandleReceived!(handle);
            }
        }

        private void HandlePush()
        {
            var item = Grab(_stage._inlet);

            if (item is ConnectionReuseItem { Decision.CanReuse: false } && _handle is not null)
            {
                // Signal the HostPoolActor (via ConnectionActor → parent forwarding)
                // that this connection should not be reused.
                _handle.ConnectionActor.Tell(new HostPoolActor.MarkConnectionNoReuse(_handle.ConnectionActor));

                if (!IsClosed(_stage._inlet) && !HasBeenPulled(_stage._inlet))
                {
                    Pull(_stage._inlet);
                }

                return;
            }

            if (item is ConnectionReuseItem)
            {
                // CanReuse = true — no action needed, just pull next.
                if (!IsClosed(_stage._inlet) && !HasBeenPulled(_stage._inlet))
                {
                    Pull(_stage._inlet);
                }

                return;
            }

            if (item is ConnectItem connect)
            {
                // Send EnsureHost — HostPoolActor will reply with ConnectionHandle to our StageActor.
                _stage.PoolRouter.Tell(
                    new PoolRouterActor.EnsureHost(connect.Key, connect.Options),
                    _stageActor!.Ref);

                // Do NOT pull — wait for ConnectionHandle reply before accepting data.
                return;
            }

            if (item is DataItem dataItem && _handle is not null)
            {
                // Write directly to the connection's outbound channel.
                _ = _handle.OutboundWriter
                    .WriteAsync(new ValueTuple<IMemoryOwner<byte>, int>(dataItem.Memory, dataItem.Length))
                    .AsTask()
                    .ContinueWith(_ => _onOutboundWriteDone!(), TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        /// <summary>
        /// Starts an async loop that reads from <see cref="ConnectionHandle.InboundReader"/>
        /// and pushes each chunk into the stage via <see cref="_onInboundData"/>.
        /// </summary>
        private void StartInboundPump()
        {
            StopInboundPump();

            var handle = _handle;
            if (handle is null)
            {
                return;
            }

            _pumpCts = new CancellationTokenSource();
            var ct = _pumpCts.Token;
            var reader = handle.InboundReader;
            var key = _currentKey;
            var onData = _onInboundData!;
            var onComplete = _onInboundComplete!;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        while (reader.TryRead(out var chunk))
                        {
                            var dataItem = new DataItem(key, chunk.Buffer, chunk.ReadableBytes);
                            onData(dataItem);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on stage shutdown.
                }

                onComplete();
            }, ct);
        }

        private void StopInboundPump()
        {
            if (_pumpCts is null) return;
            _pumpCts.Cancel();
            _pumpCts.Dispose();
            _pumpCts = null;
        }

        public override void PostStop()
        {
            StopInboundPump();
        }
    }
}