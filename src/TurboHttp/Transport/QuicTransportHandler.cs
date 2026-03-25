using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TurboHttp.Internal;
using TurboHttp.Pooling;

// QuicConnectionManager and related QUIC APIs are platform-guarded; this file suppresses
// those diagnostics because QUIC usage is gated at runtime via ConnectItem.Options being QuicOptions.
#pragma warning disable CA1416

namespace TurboHttp.Transport;

/// <summary>
/// Handles QUIC multi-stream transport (HTTP/3) for <see cref="ConnectionStage"/>.
/// Encapsulates <see cref="QuicConnectionManager"/> lifecycle, request/control/encoder stream
/// handle management, typed stream routing via <see cref="Http3TaggedItem"/>, and multiple
/// inbound pump management.
/// </summary>
internal sealed class QuicTransportHandler : ITransportHandler
{
    // ── QUIC state ──

    private QuicConnectionManager? _quicManager;
    private ConnectionHandle? _requestHandle;
    private ConnectionHandle? _controlHandle;
    private ConnectionHandle? _encoderHandle;

    /// <summary>Pending control items buffered before control stream is ready.</summary>
    private readonly Queue<(IMemoryOwner<byte> Memory, int Length, RequestEndpoint Key)> _pendingControlItems = new();

    /// <summary>Pending QPACK encoder items buffered before encoder stream is ready.</summary>
    private readonly Queue<(IMemoryOwner<byte> Memory, int Length, RequestEndpoint Key)> _pendingEncoderItems = new();

    /// <summary>All active leases for QUIC streams (disposed on Cleanup).</summary>
    private readonly List<ConnectionLease> _activeLeases = [];

    /// <summary>Cancellation tokens for all QUIC inbound pumps.</summary>
    private readonly List<CancellationTokenSource> _quicPumpCancellations = [];

    /// <summary>Pending typed stream type being opened (Control or QpackEncoder).</summary>
    private OutputStreamType? _pendingTypedStreamType;

    private RequestEndpoint _currentKey;
    private ConnectItem? _pendingConnect;

    // ── Async callbacks (registered in Initialize) ──

    private Action<ConnectionLease>? _onRequestLeaseAcquired;
    private Action<ConnectionLease>? _onTypedLeaseAcquired;
    private Action<IInputItem>? _onInboundData;
    private Action? _onOutboundWriteDone;
    private Action<Exception>? _onOutboundWriteFailed;
    private Action<Exception>? _onAcquisitionFailed;
    private Action<(TlsCloseKind CloseKind, int Gen)>? _onInboundComplete;
    private Action<QuicConnectionManager.InboundStream>? _onInboundStreamReady;

    private IStageCallbacks? _callbacks;

    /// <inheritdoc/>
    public void Initialize(IStageCallbacks callbacks)
    {
        _callbacks = callbacks;

        _onRequestLeaseAcquired = callbacks.GetAsyncCallback<ConnectionLease>(lease =>
        {
            callbacks.CancelConnectTimeout();

            if (_pendingConnect is null && _requestHandle is not null)
            {
                return;
            }

            _pendingConnect = null;

            _activeLeases.Add(lease);
            _requestHandle = lease.Handle;
            _currentKey = lease.Key;
            StartQuicInboundPump(lease.Handle, InputStreamType.Request);

            // Open control and QPACK encoder streams via QuicConnectionManager.
            OpenTypedStream(OutputStreamType.Control);
            OpenTypedStream(OutputStreamType.QpackEncoder);

            // Subscribe to server-initiated inbound streams.
            _quicManager?.StartInboundAcceptLoop(inbound => _onInboundStreamReady!(inbound));

            // Ready to process data items — pull next element.
            callbacks.SignalPullInput();
        });

        _onTypedLeaseAcquired = callbacks.GetAsyncCallback<ConnectionLease>(lease =>
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
        });

        _onInboundData = callbacks.GetAsyncCallback<IInputItem>(item =>
        {
            callbacks.PushOutput(item);
        });

        _onOutboundWriteDone = callbacks.GetAsyncCallback(() =>
        {
            callbacks.SignalPullInput();
        });

        _onOutboundWriteFailed = callbacks.GetAsyncCallback<Exception>(ex =>
        {
            callbacks.LogWarning("ConnectionStage: Outbound write failed — {0}", ex.Message);

            // Emit close signal downstream so decoder stages know the connection is dead.
            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _currentKey };
            callbacks.PushOutput(signal);

            // Clear handles so the next ConnectItem re-acquires a fresh connection.
            _requestHandle = null;
            _controlHandle = null;
            _encoderHandle = null;
        });

        _onAcquisitionFailed = callbacks.GetAsyncCallback<Exception>(ex =>
        {
            callbacks.CancelConnectTimeout();
            callbacks.LogWarning("ConnectionStage: Connection acquisition failed — {0}", ex.Message);

            if (_pendingConnect is null)
            {
                return;
            }

            // Emit close signal so the decoder/correlation stage fails the pending request.
            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
            _pendingConnect = null;

            callbacks.PushOutput(signal);

            // Accept next element from upstream.
            callbacks.SignalPullInput();
        });

        _onInboundComplete = callbacks.GetAsyncCallback<(TlsCloseKind CloseKind, int Gen)>(tuple =>
        {
            var (closeKind, _) = tuple;

            // Emit close signal to downstream decoder stages before clearing the handle.
            var signal = new CloseSignalItem(closeKind) { Key = _currentKey };
            callbacks.PushOutput(signal);

            // Connection closed — clear handles so next ConnectItem re-acquires.
            _requestHandle = null;
            _controlHandle = null;
            _encoderHandle = null;
        });

        _onInboundStreamReady = callbacks.GetAsyncCallback<QuicConnectionManager.InboundStream>(inbound =>
        {
            _activeLeases.Add(inbound.Lease);
            StartQuicInboundPump(inbound.Lease.Handle, inbound.StreamType);
        });
    }

    /// <inheritdoc/>
    public void HandleConnectItem(ConnectItem connect)
    {
        _pendingConnect = connect;

        if (connect.Options is not QuicOptions quicOptions)
        {
            _onAcquisitionFailed!(new InvalidOperationException(
                "QuicTransportHandler received a non-QuicOptions ConnectItem."));
            return;
        }

        _quicManager = new QuicConnectionManager(quicOptions, connect.Key);
        AcquireQuicConnection(connect);
        // Do NOT pull — wait for request stream lease before accepting data.
    }

    /// <inheritdoc/>
    public void HandleDataItem(DataItem dataItem)
    {
        // Untagged DataItem defaults to the request stream in QUIC mode.
        WriteToHandle(_requestHandle, dataItem.Memory, dataItem.Length);
    }

    /// <inheritdoc/>
    public void HandleTaggedItem(Http3TaggedItem tagged)
    {
        if (tagged.Inner is not DataItem dataItem)
        {
            // Non-data tagged items (control signals) — no routing needed.
            _callbacks!.SignalPullInput();
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

    /// <inheritdoc/>
    /// <remarks>QUIC connection lifecycle is managed by <see cref="QuicConnectionManager"/> — this is a no-op.</remarks>
    public void HandleConnectionReuseItem(ConnectionReuseItem reuseItem)
    {
        _callbacks!.SignalPullInput();
    }

    /// <inheritdoc/>
    /// <remarks>QUIC transport manages stream concurrency internally — this is a no-op.</remarks>
    public void HandleMaxConcurrentStreamsItem(MaxConcurrentStreamsItem item)
    {
        _callbacks!.SignalPullInput();
    }

    /// <inheritdoc/>
    /// <remarks>QUIC stream acquisition is handled by <see cref="QuicConnectionManager"/> — this is a no-op.</remarks>
    public void HandleStreamAcquireItem(StreamAcquireItem item)
    {
        _callbacks!.SignalPullInput();
    }

    /// <inheritdoc/>
    public void OnUpstreamFinished()
    {
        StopAllQuicPumps();
        _callbacks!.RequestCompleteStage();
    }

    /// <inheritdoc/>
    public void OnConnectTimeout()
    {
        if (_pendingConnect is null)
        {
            return;
        }

        _callbacks!.LogWarning(
            "ConnectionStage: Connection acquisition timed out for {0}:{1}",
            _pendingConnect.Key.Host,
            _pendingConnect.Key.Port);

        // Emit close signal so the decoder/correlation stage fails the pending request.
        // The stream stays alive — future ConnectItems can still succeed.
        var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
        _pendingConnect = null;

        _callbacks.PushOutput(signal);

        // Accept next element from upstream.
        _callbacks.SignalPullInput();
    }

    /// <inheritdoc/>
    public void Cleanup()
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
    }

    // ══════════════════════════════════════════════════════════════════
    //  Private helpers
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Acquires a QUIC request stream from <see cref="QuicConnectionManager"/>.
    /// </summary>
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

        _callbacks!.ScheduleConnectTimeout(timeout);
    }

    /// <summary>
    /// Opens a typed QUIC stream (Control or QpackEncoder) via <see cref="QuicConnectionManager"/>.
    /// </summary>
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
                    _callbacks!.LogWarning("ConnectionStage: Failed to open {0} stream — {1}",
                        streamType, t.Exception!.GetBaseException().Message);
                }
            },
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Starts an async pump for a QUIC stream that reads from <see cref="ConnectionHandle.InboundReader"/>
    /// and pushes each chunk into the stage, tagged with the appropriate <see cref="InputStreamType"/>.
    /// </summary>
    private void StartQuicInboundPump(ConnectionHandle handle, InputStreamType streamType)
    {
        var cts = new CancellationTokenSource();
        _quicPumpCancellations.Add(cts);

        var ct = cts.Token;
        var reader = handle.InboundReader;
        var key = _currentKey;
        var gen = 0; // QUIC does not use connection generation — pass 0 as placeholder
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
                onComplete((closeKind, gen));
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

    /// <summary>
    /// Writes a byte buffer to the given <see cref="ConnectionHandle"/>'s outbound channel.
    /// Logs a warning and pulls input if the handle is null.
    /// </summary>
    private void WriteToHandle(ConnectionHandle? handle, IMemoryOwner<byte> memory, int length)
    {
        if (handle is null)
        {
            _callbacks!.LogWarning(
                "ConnectionStage: Data received but no ConnectionHandle is available — dropping element.");
            _callbacks.SignalPullInput();
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
    /// Flushes buffered QUIC items to a newly-available handle, then pulls for more input.
    /// </summary>
    private void FlushPendingQuicItems(
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
        if (!_callbacks!.IsInputClosed() && !_callbacks.HasInputBeenPulled())
        {
            _callbacks.SignalPullInput();
        }
    }
}
