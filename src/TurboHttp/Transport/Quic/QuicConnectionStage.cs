using System.Threading.Channels;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Transport.Connection;

// QUIC APIs are platform-guarded; usage is gated at runtime via ConnectItem.Options being QuicOptions.
#pragma warning disable CA1416

namespace TurboHttp.Transport.Quic;

/// <summary>
/// Transport stage for HTTP/3 (QUIC). Manages multi-stream I/O (request, control, encoder),
/// tagged item routing, and multiple inbound pumps. Connection lifecycle (stream opening,
/// provider management) is handled by <see cref="QuicConnectionManager"/> directly —
/// no actor needed for QUIC since it multiplexes natively.
/// </summary>
internal sealed class QuicConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly Inlet<IOutputItem> _in = new("QuicConnection.In");
    private readonly Outlet<IInputItem> _out = new("QuicConnection.Out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public QuicConnectionStage()
    {
        Shape = new FlowShape<IOutputItem, IInputItem>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic
    {
        private const string ConnectTimerKey = "connect-timeout";

        private readonly QuicConnectionStage _stage;

        private readonly Queue<IInputItem> _pendingReads = new();

        private QuicConnectionManager? _quicManager;
        private ConnectionHandle? _requestHandle;
        private ConnectionHandle? _controlHandle;
        private ConnectionHandle? _encoderHandle;

        /// <summary>Pending control items buffered before control stream is ready.</summary>
        private readonly Queue<NetworkBuffer> _pendingControlItems = new();

        /// <summary>Pending QPACK encoder items buffered before encoder stream is ready.</summary>
        private readonly Queue<NetworkBuffer> _pendingEncoderItems = new();

        /// <summary>All active leases for QUIC streams (disposed on Cleanup).</summary>
        private readonly List<ConnectionLease> _activeLeases = [];

        /// <summary>Cancellation tokens for all QUIC inbound pumps.</summary>
        private readonly List<CancellationTokenSource> _quicPumpCancellations = [];

        /// <summary>Pending typed stream type being opened (Control or QpackEncoder).</summary>
        private OutputStreamType? _pendingTypedStreamType;

        private RequestEndpoint _currentKey;
        private ConnectItem? _pendingConnect;

        /// <summary>NetworkBuffers buffered before the request handle is available.</summary>
        private readonly Queue<NetworkBuffer> _pendingWrites = new();

        private Action<ConnectionLease>? _onRequestLeaseAcquired;
        private Action<ConnectionLease>? _onTypedLeaseAcquired;
        private Action<IInputItem>? _onInboundData;
        private Action? _onOutboundWriteDone;
        private Action<Exception>? _onOutboundWriteFailed;
        private Action<Exception>? _onAcquisitionFailed;
        private Action<(TlsCloseKind CloseKind, int Gen)>? _onInboundComplete;
        private Action<QuicConnectionManager.InboundStream>? _onInboundStreamReady;

        public Logic(QuicConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    StopAllQuicPumps();
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
                    CleanupTransport();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            RegisterAsyncCallbacks();
            Pull(_stage._in);
        }


        private void RegisterAsyncCallbacks()
        {
            _onRequestLeaseAcquired = GetAsyncCallback<ConnectionLease>(OnRequestLeaseAcquired);
            _onTypedLeaseAcquired = GetAsyncCallback<ConnectionLease>(OnTypedLeaseAcquired);
            _onInboundData = GetAsyncCallback<IInputItem>(PushOutput);
            _onOutboundWriteDone = GetAsyncCallback(SignalPullInput);
            _onOutboundWriteFailed = GetAsyncCallback<Exception>(OnOutboundWriteFailed);
            _onAcquisitionFailed = GetAsyncCallback<Exception>(OnAcquisitionFailed);
            _onInboundComplete = GetAsyncCallback<(TlsCloseKind, int)>(OnInboundComplete);
            _onInboundStreamReady = GetAsyncCallback<QuicConnectionManager.InboundStream>(OnInboundStreamReady);
        }


        private void HandlePush()
        {
            var item = Grab(_stage._in);

            switch (item)
            {
                case ConnectItem connect:
                    HandleConnectItem(connect);
                    break;

                case Http3OutputTaggedItem tagged:
                    HandleTaggedItem(tagged);
                    break;

                case NetworkBuffer dataItem:
                    HandleDataItem(dataItem);
                    break;

                case ConnectionReuseItem reuseItem:
                    // QUIC manages these internally — no-op
                    reuseItem.Return();
                    SignalPullInput();
                    break;

                case StreamAcquireItem acquireItem:
                    // QUIC manages these internally — no-op
                    acquireItem.Return();
                    SignalPullInput();
                    break;

                case MaxConcurrentStreamsItem:
                    // QUIC manages these internally — no-op
                    SignalPullInput();
                    break;
            }
        }


        private void HandleConnectItem(ConnectItem connect)
        {
            Log.Debug("QuicConnectionStage: ConnectItem key={0}:{1}", connect.Key.Host, connect.Key.Port);

            CleanupTransport();
            _pendingConnect = connect;

            if (connect.Options is not QuicOptions quicOptions)
            {
                _onAcquisitionFailed!(new InvalidOperationException(
                    "QuicConnectionStage received a non-QuicOptions ConnectItem."));
                return;
            }

            _quicManager = new QuicConnectionManager(quicOptions, connect.Key);
            AcquireQuicConnection(connect);
        }


        private void HandleDataItem(NetworkBuffer dataItem)
        {
            if (_requestHandle is null)
            {
                _pendingWrites.Enqueue(dataItem);
                SignalPullInput();
                return;
            }

            WriteToHandle(_requestHandle, dataItem);
        }


        private void HandleTaggedItem(Http3OutputTaggedItem outputTagged)
        {
            if (outputTagged.Inner is not NetworkBuffer dataItem)
            {
                SignalPullInput();
                return;
            }

            switch (outputTagged.StreamType)
            {
                case OutputStreamType.Request:
                    WriteToHandle(_requestHandle, dataItem);
                    break;

                case OutputStreamType.Control:
                    if (_controlHandle is not null)
                    {
                        WriteToHandle(_controlHandle, dataItem);
                    }
                    else
                    {
                        _pendingControlItems.Enqueue(dataItem);
                    }

                    break;

                case OutputStreamType.QpackEncoder:
                    if (_encoderHandle is not null)
                    {
                        WriteToHandle(_encoderHandle, dataItem);
                    }
                    else
                    {
                        _pendingEncoderItems.Enqueue(dataItem);
                    }

                    break;
            }
        }


        protected override void OnTimer(object timerKey)
        {
            if (timerKey is not string key || key != ConnectTimerKey)
            {
                return;
            }

            if (_pendingConnect is null)
            {
                return;
            }

            Log.Warning("QuicConnectionStage: Connection acquisition timed out for {0}:{1}",
                _pendingConnect.Key.Host, _pendingConnect.Key.Port);

            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
            _pendingConnect = null;

            PushOutput(signal);
            SignalPullInput();
        }


        private void OnRequestLeaseAcquired(ConnectionLease lease)
        {
            CancelTimer(ConnectTimerKey);

            if (_pendingConnect is null && _requestHandle is not null)
            {
                return;
            }

            _pendingConnect = null;

            _activeLeases.Add(lease);
            _requestHandle = lease.Handle;
            _currentKey = lease.Key;
            StartQuicInboundPump(lease.Handle, InputStreamType.Request);

            // Open control and QPACK encoder streams
            OpenTypedStream(OutputStreamType.Control);
            OpenTypedStream(OutputStreamType.QpackEncoder);

            // Subscribe to server-initiated inbound streams
            _quicManager?.StartInboundAcceptLoop(inbound => _onInboundStreamReady!(inbound));

            // Flush any NetworkBuffers buffered before the request handle was available
            while (_pendingWrites.TryDequeue(out var buffered))
            {
                WriteToHandle(_requestHandle, buffered);
            }

            SignalPullInput();
        }

        private void OnTypedLeaseAcquired(ConnectionLease lease)
        {
            _activeLeases.Add(lease);
            var streamType = _pendingTypedStreamType;
            _pendingTypedStreamType = null;

            switch (streamType)
            {
                case OutputStreamType.Control:
                    _controlHandle = lease.Handle;
                    FlushPendingQuicItems(_pendingControlItems, lease.Handle);
                    break;

                case OutputStreamType.QpackEncoder:
                    _encoderHandle = lease.Handle;
                    FlushPendingQuicItems(_pendingEncoderItems, lease.Handle);
                    break;
            }
        }

        private void OnOutboundWriteFailed(Exception ex)
        {
            Log.Warning("QuicConnectionStage: Outbound write failed — {0}", ex.Message);

            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _currentKey };
            PushOutput(signal);

            _requestHandle = null;
            _controlHandle = null;
            _encoderHandle = null;
        }

        private void OnAcquisitionFailed(Exception ex)
        {
            CancelTimer(ConnectTimerKey);
            Log.Warning("QuicConnectionStage: Connection acquisition failed — {0}", ex.Message);

            if (_pendingConnect is null)
            {
                return;
            }

            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
            _pendingConnect = null;

            PushOutput(signal);
            SignalPullInput();
        }

        private void OnInboundComplete((TlsCloseKind CloseKind, int Gen) tuple)
        {
            var (closeKind, _) = tuple;

            var signal = new CloseSignalItem(closeKind) { Key = _currentKey };
            PushOutput(signal);

            _requestHandle = null;
            _controlHandle = null;
            _encoderHandle = null;
        }

        private void OnInboundStreamReady(QuicConnectionManager.InboundStream inbound)
        {
            _activeLeases.Add(inbound.Lease);
            StartQuicInboundPump(inbound.Lease.Handle, inbound.StreamType);
        }


        public override void PostStop()
        {
            CancelTimer(ConnectTimerKey);
            CleanupTransport();

            while (_pendingWrites.TryDequeue(out var orphan))
            {
                orphan.Dispose();
            }
        }


        private void AcquireQuicConnection(ConnectItem connect)
        {
            var manager = _quicManager;
            if (manager is null)
            {
                _onAcquisitionFailed!(new InvalidOperationException("QuicConnectionManager not initialized"));
                return;
            }

            var acquireTask = manager.OpenStreamAsync(OutputStreamType.Request);

            acquireTask.ContinueWith(
                t => _onRequestLeaseAcquired!(t.Result),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            acquireTask.ContinueWith(
                t => _onAcquisitionFailed!(t.Exception!.GetBaseException()),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);

            var timeout = connect.Options.ConnectTimeout;
            if (timeout <= TimeSpan.Zero)
            {
                timeout = TimeSpan.FromSeconds(10);
            }

            ScheduleOnce(ConnectTimerKey, timeout);
        }

        private void OpenTypedStream(OutputStreamType streamType)
        {
            var manager = _quicManager;
            if (manager is null)
            {
                return;
            }

            _pendingTypedStreamType = streamType;
            var openTask = manager.OpenStreamAsync(streamType);

            openTask.ContinueWith(
                t => _onTypedLeaseAcquired!(t.Result),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            openTask.ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                    {
                        Log.Warning("QuicConnectionStage: Failed to open {0} stream — {1}",
                            streamType, t.Exception!.GetBaseException().Message);
                    }
                },
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
        }

        private void CleanupTransport()
        {
            StopAllQuicPumps();

            foreach (var lease in _activeLeases)
            {
                lease.Dispose();
            }

            _activeLeases.Clear();

            if (_quicManager is { } manager)
            {
                _ = manager.DisposeAsync();
                _quicManager = null;
            }

            _requestHandle = null;
            _controlHandle = null;
            _encoderHandle = null;
        }


        private void StartQuicInboundPump(ConnectionHandle handle, InputStreamType streamType)
        {
            var cts = new CancellationTokenSource();
            _quicPumpCancellations.Add(cts);

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
                            chunk.Key = key;

                            IInputItem outputItem = streamType == InputStreamType.Request
                                ? chunk
                                : new Http3InputTaggedItem(chunk, streamType);

                            onData(outputItem);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ChannelClosedException ex) when (ex.InnerException is AbruptCloseException)
                {
                    closeKind = TlsCloseKind.AbruptClose;
                }

                // Only emit close signal for the request stream (main connection lifecycle)
                if (streamType == InputStreamType.Request)
                {
                    onComplete((closeKind, 0));
                }
            }, ct);
        }

        private void StopAllQuicPumps()
        {
            foreach (var cts in _quicPumpCancellations)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _quicPumpCancellations.Clear();
        }


        private void WriteToHandle(ConnectionHandle? handle, NetworkBuffer buffer)
        {
            if (handle is null)
            {
                Log.Warning("QuicConnectionStage: Data received but no handle available — dropping element.");
                SignalPullInput();
                return;
            }

            var vt = handle.OutboundWriter.WriteAsync(buffer);

            if (vt.IsCompletedSuccessfully)
            {
                _onOutboundWriteDone!();
                return;
            }

            var writeTask = vt.AsTask();

            writeTask.ContinueWith(
                _ => _onOutboundWriteDone!(),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            writeTask.ContinueWith(
                t => _onOutboundWriteFailed!(t.Exception!.GetBaseException()),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
        }

        private void FlushPendingQuicItems(
            Queue<NetworkBuffer> pending,
            ConnectionHandle handle)
        {
            while (pending.TryDequeue(out var item))
            {
                var vt = handle.OutboundWriter.WriteAsync(item);

                if (vt.IsCompletedSuccessfully)
                {
                    continue;
                }

                vt.AsTask().ContinueWith(
                    t =>
                    {
                        if (t.IsFaulted)
                        {
                            _onOutboundWriteFailed!(t.Exception!.GetBaseException());
                        }
                    },
                    TaskContinuationOptions.ExecuteSynchronously);
            }

            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                SignalPullInput();
            }
        }


        private void PushOutput(IInputItem item)
        {
            if (IsAvailable(_stage._out))
            {
                Push(_stage._out, item);
            }
            else
            {
                _pendingReads.Enqueue(item);
            }
        }

        private void SignalPullInput()
        {
            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }
    }
}
