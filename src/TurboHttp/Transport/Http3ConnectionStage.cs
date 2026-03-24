using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
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
/// Uses <see cref="QuicConnectionManager"/> for actor-free QUIC stream management.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
public sealed class Http3ConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private QuicConnectionManager? QuicManager { get; set; }

    private readonly Inlet<IOutputItem> _in = new("Http3Connection.In");
    private readonly Outlet<IInputItem> _out = new("Http3Connection.Out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public Http3ConnectionStage()
    {
        Shape = new FlowShape<IOutputItem, IInputItem>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic
    {
        private const string ConnectTimerKey = "connect-timeout";

        private readonly Http3ConnectionStage _stage;
        private readonly Queue<IInputItem> _pendingReads = new();

        // --- Outbound handles (one per stream type) ---
        private ConnectionHandle? _requestHandle;
        private ConnectionHandle? _controlHandle;
        private ConnectionHandle? _encoderHandle;

        // --- Outbound buffers for items arriving before handle is ready ---
        private readonly Queue<(IMemoryOwner<byte> Memory, int Length, RequestEndpoint Key)> _pendingControlItems = new();
        private readonly Queue<(IMemoryOwner<byte> Memory, int Length, RequestEndpoint Key)> _pendingEncoderItems = new();

        // --- Async callbacks ---
        private Action<IInputItem>? _onInboundData;
        private Action? _onOutboundWriteDone;
        private Action<Exception>? _onOutboundWriteFailed;
        private Action<ConnectionLease>? _onRequestLeaseAcquired;
        private Action<ConnectionLease>? _onTypedLeaseAcquired;
        private Action<Exception>? _onAcquisitionFailed;
        private Action<TlsCloseKind>? _onInboundComplete;
        private Action<QuicConnectionManager.InboundStream>? _onInboundStreamReady;

        private readonly List<CancellationTokenSource> _pumpCancellations = [];
        private readonly List<ConnectionLease> _activeLeases = [];

        /// <summary>The RequestEndpoint from the most recent ConnectItem.</summary>
        private RequestEndpoint _currentKey;

        /// <summary>The ConnectItem currently awaiting a connection.</summary>
        private ConnectItem? _pendingConnect;

        /// <summary>Pending typed stream type being opened (Control or QpackEncoder).</summary>
        private OutputStreamType? _pendingTypedStreamType;

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
            });

            _onRequestLeaseAcquired = GetAsyncCallback<ConnectionLease>(lease =>
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
                StartInboundPump(lease.Handle, InputStreamType.Request);

                // Open control and QPACK encoder streams via QuicConnectionManager.
                OpenTypedStream(OutputStreamType.Control);
                OpenTypedStream(OutputStreamType.QpackEncoder);

                // Subscribe to server-initiated inbound streams.
                var manager = _stage.QuicManager;
                if (manager is not null)
                {
                    manager.StartInboundAcceptLoop(inbound => _onInboundStreamReady!(inbound));
                }

                // Ready to process data items — pull next element.
                if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
                {
                    Pull(_stage._in);
                }
            });

            _onTypedLeaseAcquired = GetAsyncCallback<ConnectionLease>(lease =>
            {
                _activeLeases.Add(lease);
                var streamType = _pendingTypedStreamType;
                _pendingTypedStreamType = null;

                switch (streamType)
                {
                    case OutputStreamType.Control:
                        _controlHandle = lease.Handle;
                        FlushPendingItems(_pendingControlItems, lease.Handle);
                        break;

                    case OutputStreamType.QpackEncoder:
                        _encoderHandle = lease.Handle;
                        FlushPendingItems(_pendingEncoderItems, lease.Handle);
                        break;
                }
            });

            _onAcquisitionFailed = GetAsyncCallback<Exception>(ex =>
            {
                CancelTimer(ConnectTimerKey);

                Log.Warning("Http3ConnectionStage: Stream acquisition failed — {0}", ex.Message);

                if (_pendingConnect is not null)
                {
                    var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
                    _pendingConnect = null;

                    if (IsAvailable(_stage._out))
                    {
                        Push(_stage._out, signal);
                    }
                    else
                    {
                        _pendingReads.Enqueue(signal);
                    }
                }

                TryPull();
            });

            _onInboundStreamReady = GetAsyncCallback<QuicConnectionManager.InboundStream>(inbound =>
            {
                _activeLeases.Add(inbound.Lease);
                StartInboundPump(inbound.Lease.Handle, inbound.StreamType);
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

                // Connection closed — clear handles so next ConnectItem re-acquires.
                _requestHandle = null;
                _controlHandle = null;
                _encoderHandle = null;
            });

            // Ready to accept ConnectItem immediately.
            Pull(_stage._in);
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

            // --- Non-tagged items ---
            if (item is MaxConcurrentStreamsItem)
            {
                // HTTP/3 does not use MAX_CONCURRENT_STREAMS via frames — QUIC transport handles this.
                TryPull();
                return;
            }

            if (item is StreamAcquireItem)
            {
                // Stream acquisition is handled by QuicConnectionManager — no action needed.
                TryPull();
                return;
            }

            if (item is ConnectionReuseItem)
            {
                // HTTP/3 connections are managed by QuicConnectionManager lifecycle.
                TryPull();
                return;
            }

            if (item is ConnectItem connect)
            {
                _pendingConnect = connect;

                // Create a new QuicConnectionManager and open the request stream.
                if (connect.Options is QuicOptions quicOptions)
                {
                    _stage.QuicManager = new QuicConnectionManager(quicOptions, connect.Key);
                }

                AcquireRequestStream(connect);

                // Do NOT pull — wait for lease before accepting data.
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
                // Non-data tagged items (control signals) — no routing needed.
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
        /// Acquires a request stream from the <see cref="QuicConnectionManager"/>.
        /// </summary>
        private void AcquireRequestStream(ConnectItem connect)
        {
            var manager = _stage.QuicManager;
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

        /// <summary>
        /// Opens a typed QUIC stream (Control or QpackEncoder) via <see cref="QuicConnectionManager"/>.
        /// </summary>
        private void OpenTypedStream(OutputStreamType streamType)
        {
            var manager = _stage.QuicManager;
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
                        Log.Warning("Http3ConnectionStage: Failed to open {0} stream — {1}",
                            streamType, t.Exception!.GetBaseException().Message);
                    }
                },
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
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

            Log.Warning(
                "Http3ConnectionStage: Connection acquisition timed out for {0}:{1}",
                _pendingConnect.Key.Host,
                _pendingConnect.Key.Port);

            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
            _pendingConnect = null;

            if (IsAvailable(_stage._out))
            {
                Push(_stage._out, signal);
            }
            else
            {
                _pendingReads.Enqueue(signal);
            }

            TryPull();
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
            CancelTimer(ConnectTimerKey);
            StopAllPumps();

            // Dispose all active leases.
            foreach (var lease in _activeLeases)
            {
                _ = lease.DisposeAsync();
            }

            _activeLeases.Clear();

            // Dispose the QuicConnectionManager.
            if (_stage.QuicManager is { } manager)
            {
                _ = manager.DisposeAsync();
                _stage.QuicManager = null;
            }
        }
    }
}
