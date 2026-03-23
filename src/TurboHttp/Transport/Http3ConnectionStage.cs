using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Pooling;

namespace TurboHttp.Transport;

/// <summary>
/// QUIC stream multiplexer for the HTTP/3 transport path.
/// Drop-in replacement for <see cref="ConnectionStage"/> that manages multiple
/// <see cref="ConnectionHandle"/> instances (one per QUIC stream type), unwraps
/// <see cref="Http3TaggedItem"/> to route outbound data to the correct stream,
/// and merges inbound data from all streams with <see cref="InputStreamType"/> tags.
/// </summary>
public sealed class Http3ConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private IActorRef PoolRouter { get; }

    private readonly Inlet<IOutputItem> _in = new("Http3Connection.In");
    private readonly Outlet<IInputItem> _out = new("Http3Connection.Out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public Http3ConnectionStage(IActorRef poolRouter)
    {
        PoolRouter = poolRouter;
        Shape = new FlowShape<IOutputItem, IInputItem>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http3ConnectionStage _stage;
        private readonly Queue<IInputItem> _pendingReads = new();

        // --- Outbound handles (one per stream type) ---
        private ConnectionHandle? _requestHandle;
        private ConnectionHandle? _controlHandle;
        private ConnectionHandle? _encoderHandle;

        // --- Inbound handles for server-initiated streams ---
        private readonly List<(ConnectionHandle Handle, InputStreamType StreamType)> _inboundHandles = [];

        // --- Outbound buffers for items arriving before handle is ready ---
        private readonly Queue<(IMemoryOwner<byte> Memory, int Length, RequestEndpoint Key)> _pendingControlItems = new();
        private readonly Queue<(IMemoryOwner<byte> Memory, int Length, RequestEndpoint Key)> _pendingEncoderItems = new();

        // --- Async callbacks ---
        private Action<IInputItem>? _onInboundData;
        private Action? _onOutboundWriteDone;
        private Action<Exception>? _onOutboundWriteFailed;
        private Action<ConnectionHandle>? _onRequestHandleReceived;
        private Action<Http3ConnectionActor.TypedConnectionHandle>? _onTypedHandleReceived;
        private Action<Http3ConnectionActor.InboundStreamReady>? _onInboundStreamReady;
        private Action<TlsCloseKind>? _onInboundComplete;

        private StageActor? _stageActor;
        private readonly List<CancellationTokenSource> _pumpCancellations = [];

        /// <summary>The RequestEndpoint from the most recent ConnectItem.</summary>
        private RequestEndpoint _currentKey;

        public Logic(Http3ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    StopAllPumps();
                    CompleteStage();
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (_pendingReads.TryDequeue(out var item))
                    {
                        Push(_stage._out, item);
                    }
                },
                onDownstreamFinish: _ =>
                {
                    StopAllPumps();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            _onInboundData = GetAsyncCallback<IInputItem>(item =>
            {
                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, item);
                }
                else
                {
                    _pendingReads.Enqueue(item);
                }
            });

            _onOutboundWriteDone = GetAsyncCallback(() =>
            {
                if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
                {
                    Pull(_stage._in);
                }
            });

            _onOutboundWriteFailed = GetAsyncCallback<Exception>(ex =>
            {
                Log.Warning("Http3ConnectionStage: Outbound write failed — {0}", ex.Message);

                // Notify the pool to tear down the QUIC connection.
                if (_requestHandle is { } h)
                {
                    h.ConnectionActor.Tell(
                        new HostPool.MarkConnectionNoReuse(h.ConnectionActor));
                    h.ConnectionActor.Tell(
                        new HostPool.StreamCompleted(h.ConnectionActor));
                }

                // Emit close signal downstream so decoder stages know the connection is dead.
                var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _currentKey };
                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, signal);
                }
                else
                {
                    _pendingReads.Enqueue(signal);
                }

                // Clear handles so the next ConnectItem re-acquires a fresh connection.
                _requestHandle = null;
                _controlHandle = null;
                _encoderHandle = null;
                _inboundHandles.Clear();
            });

            _onRequestHandleReceived = GetAsyncCallback<ConnectionHandle>(handle =>
            {
                _requestHandle = handle;
                _currentKey = handle.Key;
                StartInboundPump(handle, InputStreamType.Request);

                // Request control and QPACK encoder streams from the connection actor.
                handle.ConnectionActor.Tell(
                    new Http3ConnectionActor.OpenTypedStream(_stageActor!.Ref, OutputStreamType.Control));
                handle.ConnectionActor.Tell(
                    new Http3ConnectionActor.OpenTypedStream(_stageActor!.Ref, OutputStreamType.QpackEncoder));

                // Subscribe to server-initiated inbound streams.
                handle.ConnectionActor.Tell(
                    new Http3ConnectionActor.SubscribeInboundStreams(_stageActor!.Ref));

                // Ready to process data items — pull next element.
                if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
                {
                    Pull(_stage._in);
                }
            });

            _onTypedHandleReceived = GetAsyncCallback<Http3ConnectionActor.TypedConnectionHandle>(msg =>
            {
                switch (msg.StreamType)
                {
                    case OutputStreamType.Control:
                        _controlHandle = msg.Handle;
                        FlushPendingItems(_pendingControlItems, msg.Handle);
                        break;

                    case OutputStreamType.QpackEncoder:
                        _encoderHandle = msg.Handle;
                        FlushPendingItems(_pendingEncoderItems, msg.Handle);
                        break;

                    case OutputStreamType.Request:
                        // QUIC: HostPool routes via OpenTypedStream for each request,
                        // so the first request handle arrives here (not as raw ConnectionHandle).
                        _onRequestHandleReceived!(msg.Handle);
                        break;
                }
            });

            _onInboundStreamReady = GetAsyncCallback<Http3ConnectionActor.InboundStreamReady>(msg =>
            {
                _inboundHandles.Add((msg.Handle, msg.StreamType));
                StartInboundPump(msg.Handle, msg.StreamType);
            });

            _onInboundComplete = GetAsyncCallback<TlsCloseKind>(closeKind =>
            {
                var signal = new CloseSignalItem(closeKind) { Key = _currentKey };
                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, signal);
                }
                else
                {
                    _pendingReads.Enqueue(signal);
                }

                // Connection closed — clear the request handle so next ConnectItem re-acquires.
                _requestHandle = null;
                _controlHandle = null;
                _encoderHandle = null;
                _inboundHandles.Clear();
            });

            _stageActor = GetStageActor(OnMessage);

            // Ready to accept ConnectItem immediately.
            Pull(_stage._in);
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            switch (args.msg)
            {
                case ConnectionHandle handle:
                    _onRequestHandleReceived!(handle);
                    break;

                case Http3ConnectionActor.TypedConnectionHandle typed:
                    _onTypedHandleReceived!(typed);
                    break;

                case Http3ConnectionActor.InboundStreamReady inbound:
                    _onInboundStreamReady!(inbound);
                    break;
            }
        }

        private void HandlePush()
        {
            var item = Grab(_stage._in);

            // --- Http3TaggedItem: unwrap and route to correct stream ---
            if (item is Http3TaggedItem tagged)
            {
                HandleTaggedItem(tagged);
                return;
            }

            // --- Non-tagged items: same behaviour as ConnectionStage ---
            var handle = _requestHandle;

            if (item is MaxConcurrentStreamsItem maxStreams)
            {
                handle?.ConnectionActor.Tell(
                    new HostPool.UpdateMaxConcurrentStreams(handle.ConnectionActor, maxStreams.MaxStreams));
                TryPull();
                return;
            }

            if (item is StreamAcquireItem)
            {
                handle?.ConnectionActor.Tell(
                    new HostPool.StreamAcquired(handle.ConnectionActor));
                TryPull();
                return;
            }

            if (item is ConnectionReuseItem reuseItem)
            {
                if (!reuseItem.Decision.CanReuse)
                {
                    handle?.ConnectionActor.Tell(
                        new HostPool.MarkConnectionNoReuse(handle.ConnectionActor));
                }

                handle?.ConnectionActor.Tell(
                    new HostPool.StreamCompleted(handle.ConnectionActor));
                TryPull();
                return;
            }

            if (item is ConnectItem connect)
            {
                // Send EnsureHost — HostPool will reply with ConnectionHandle to our StageActor.
                _stage.PoolRouter.Tell(
                    new PoolRouter.EnsureHost(connect.Key, connect.Options),
                    _stageActor!.Ref);

                // Do NOT pull — wait for ConnectionHandle reply before accepting data.
                return;
            }

            if (item is DataItem dataItem)
            {
                // Untagged DataItem defaults to request stream.
                WriteToHandle(_requestHandle, dataItem.Memory, dataItem.Length);
            }
        }

        private void HandleTaggedItem(Http3TaggedItem tagged)
        {
            if (tagged.Inner is not DataItem dataItem)
            {
                // Non-data tagged items (control signals) — forward to appropriate actor.
                TryPull();
                return;
            }

            switch (tagged.StreamType)
            {
                case OutputStreamType.Request:
                    WriteToHandle(_requestHandle, dataItem.Memory, dataItem.Length);
                    break;

                case OutputStreamType.Control:
                    if (_controlHandle is not null)
                    {
                        WriteToHandle(_controlHandle, dataItem.Memory, dataItem.Length);
                    }
                    else
                    {
                        _pendingControlItems.Enqueue((dataItem.Memory, dataItem.Length, dataItem.Key));
                    }
                    break;

                case OutputStreamType.QpackEncoder:
                    if (_encoderHandle is not null)
                    {
                        WriteToHandle(_encoderHandle, dataItem.Memory, dataItem.Length);
                    }
                    else
                    {
                        _pendingEncoderItems.Enqueue((dataItem.Memory, dataItem.Length, dataItem.Key));
                    }
                    break;
            }
        }

        private void WriteToHandle(ConnectionHandle? handle, IMemoryOwner<byte> memory, int length)
        {
            if (handle is null)
            {
                Log.Warning("Http3ConnectionStage: Data received but no ConnectionHandle is available — dropping element.");
                TryPull();
                return;
            }

            var writeTask = handle.OutboundWriter
                .WriteAsync(new ValueTuple<IMemoryOwner<byte>, int>(memory, length))
                .AsTask();

            writeTask.ContinueWith(
                _ => _onOutboundWriteDone!(),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            writeTask.ContinueWith(
                t => _onOutboundWriteFailed!(t.Exception!.GetBaseException()),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Flushes buffered items to a newly-available handle, then pulls for more input.
        /// </summary>
        private void FlushPendingItems(
            Queue<(IMemoryOwner<byte> Memory, int Length, RequestEndpoint Key)> pending,
            ConnectionHandle handle)
        {
            while (pending.TryDequeue(out var item))
            {
                var writeTask = handle.OutboundWriter
                    .WriteAsync(new ValueTuple<IMemoryOwner<byte>, int>(item.Memory, item.Length))
                    .AsTask();

                writeTask.ContinueWith(
                    t =>
                    {
                        if (t.IsFaulted)
                        {
                            _onOutboundWriteFailed!(t.Exception!.GetBaseException());
                        }
                    },
                    TaskContinuationOptions.ExecuteSynchronously);
            }

            // If we were waiting to pull because items were buffered, do so now.
            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        private void TryPull()
        {
            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        /// <summary>
        /// Starts an async pump that reads from a <see cref="ConnectionHandle.InboundReader"/>
        /// and pushes each chunk into the stage, tagged with the appropriate <see cref="InputStreamType"/>.
        /// </summary>
        private void StartInboundPump(ConnectionHandle handle, InputStreamType streamType)
        {
            var cts = new CancellationTokenSource();
            _pumpCancellations.Add(cts);

            var ct = cts.Token;
            var reader = handle.InboundReader;
            var key = _currentKey;
            var onData = _onInboundData!;
            var onComplete = _onInboundComplete!;

            _ = Task.Run(async () =>
            {
                var closeKind = TlsCloseKind.CleanClose;
                try
                {
                    while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        while (reader.TryRead(out var chunk))
                        {
                            var dataItem = new DataItem(chunk.Buffer, chunk.ReadableBytes) { Key = key };

                            IInputItem outputItem = streamType == InputStreamType.Request
                                ? dataItem
                                : new Http3InputTaggedItem(dataItem, streamType);

                            onData(outputItem);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on stage shutdown — do not emit close signal.
                    return;
                }
                catch (ChannelClosedException ex) when (ex.InnerException is AbruptCloseException)
                {
                    closeKind = TlsCloseKind.AbruptClose;
                }

                // Only emit close signal for the request stream (main connection lifecycle).
                if (streamType == InputStreamType.Request)
                {
                    onComplete(closeKind);
                }
            }, ct);
        }

        private void StopAllPumps()
        {
            foreach (var cts in _pumpCancellations)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _pumpCancellations.Clear();
        }

        public override void PostStop()
        {
            StopAllPumps();
        }
    }
}
