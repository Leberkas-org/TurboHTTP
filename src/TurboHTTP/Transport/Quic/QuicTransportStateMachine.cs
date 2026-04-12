using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

// QUIC APIs are platform-guarded; usage is gated at runtime via ConnectItem.Options being QuicOptions.
#pragma warning disable CA1416

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Encapsulates all QUIC transport state and logic — multi-stream I/O (request, control, encoder),
/// tagged item routing, and connection lifecycle management.
/// Calls back into <see cref="IQuicTransportOperations"/> for Akka-specific operations
/// (Push, Pull, Timer, Complete, Fail).
/// Async events arrive via <see cref="Dispatch"/> after being marshaled through the StageActorRef.
/// </summary>
internal sealed class QuicTransportStateMachine
{
    private const string ConnectTimerKey = "connect-timeout";

    private readonly IQuicTransportOperations _ops;
    private readonly IActorRef _self;

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

    public QuicTransportStateMachine(
        IQuicTransportOperations ops,
        IActorRef self)
    {
        _ops = ops;
        _self = self;
    }

    // ─── Event Dispatch ───

    public void Dispatch(QuicTransportEvent evt)
    {
        switch (evt)
        {
            case QuicTransportEvent.RequestLeaseAcquired e:
                OnRequestLeaseAcquired(e.Lease);
                break;
            case QuicTransportEvent.TypedLeaseAcquired e:
                OnTypedLeaseAcquired(e.Lease);
                break;
            case QuicTransportEvent.AcquisitionFailed e:
                OnAcquisitionFailed(e.Error);
                break;
            case QuicTransportEvent.InboundData e:
                _ops.OnPushOutput(e.Item);
                break;
            case QuicTransportEvent.InboundComplete e:
                OnInboundComplete(e.CloseKind);
                break;
            case QuicTransportEvent.InboundPumpFailed e:
                _ops.OnFailStage(e.Error);
                break;
            case QuicTransportEvent.InboundStreamReady e:
                OnInboundStreamReady(e.Stream);
                break;
            case QuicTransportEvent.OutboundWriteDone:
                _ops.OnSignalPullInput();
                break;
            case QuicTransportEvent.OutboundWriteFailed e:
                OnOutboundWriteFailed(e.Error);
                break;
        }
    }

    // ─── Upstream Handlers ───

    public void HandlePush(IOutputItem item)
    {
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

            case ConnectionReuseItem:
            case StreamAcquireItem:
            case MaxConcurrentStreamsItem:
                // QUIC manages these internally — no-op
                _ops.OnSignalPullInput();
                break;
        }
    }

    public void HandleUpstreamFinish()
    {
        StopAllQuicPumps();
        _ops.OnCompleteStage();
    }

    public void HandleDownstreamFinish()
    {
        CleanupTransport();
    }

    // ─── Item Handlers ───

    private void HandleConnectItem(ConnectItem connect)
    {
        _ops.Log.Debug("QuicConnectionStage: ConnectItem key={0}:{1}", connect.Key.Host, connect.Key.Port);

        CleanupTransport();
        _pendingConnect = connect;

        if (connect.Options is not QuicOptions quicOptions)
        {
            _self.Tell(new QuicTransportEvent.AcquisitionFailed(new InvalidOperationException(
                "QuicConnectionStage received a non-QuicOptions ConnectItem.")));
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
            _ops.OnSignalPullInput();
            return;
        }

        WriteToHandle(_requestHandle, dataItem);
    }

    private void HandleTaggedItem(Http3OutputTaggedItem outputTagged)
    {
        if (outputTagged.Inner is not NetworkBuffer dataItem)
        {
            _ops.OnSignalPullInput();
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

    // ─── Timer ───

    public void OnTimer(string? timerKey)
    {
        if (timerKey != ConnectTimerKey)
        {
            return;
        }

        if (_pendingConnect is null)
        {
            return;
        }

        _ops.Log.Warning("QuicConnectionStage: Connection acquisition timed out for {0}:{1}",
            _pendingConnect.Value.Key.Host, _pendingConnect.Value.Key.Port);

        var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Value.Key };
        _pendingConnect = null;

        _ops.OnPushOutput(signal);
        _ops.OnSignalPullInput();
    }

    // ─── Async Event Handlers ───

    private void OnRequestLeaseAcquired(ConnectionLease lease)
    {
        _ops.OnCancelTimer(ConnectTimerKey);

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
        var self = _self;
        _quicManager?.StartInboundAcceptLoop(inbound =>
            self.Tell(new QuicTransportEvent.InboundStreamReady(inbound)));

        // Flush any NetworkBuffers buffered before the request handle was available
        while (_pendingWrites.TryDequeue(out var buffered))
        {
            WriteToHandle(_requestHandle, buffered);
        }

        _ops.OnSignalPullInput();
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
        _ops.Log.Warning("QuicConnectionStage: Outbound write failed — {0}", ex.Message);

        var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _currentKey };
        _ops.OnPushOutput(signal);

        _requestHandle = null;
        _controlHandle = null;
        _encoderHandle = null;
    }

    private void OnAcquisitionFailed(Exception ex)
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        _ops.Log.Warning("QuicConnectionStage: Connection acquisition failed — {0}", ex.Message);

        if (_pendingConnect is null)
        {
            return;
        }

        var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Value.Key };
        _pendingConnect = null;

        _ops.OnPushOutput(signal);
        _ops.OnSignalPullInput();
    }

    private void OnInboundComplete(TlsCloseKind closeKind)
    {
        var signal = new CloseSignalItem(closeKind) { Key = _currentKey };
        _ops.OnPushOutput(signal);

        _requestHandle = null;
        _controlHandle = null;
        _encoderHandle = null;
    }

    private void OnInboundStreamReady(QuicConnectionManager.InboundStream inbound)
    {
        _activeLeases.Add(inbound.Lease);
        StartQuicInboundPump(inbound.Lease.Handle, inbound.StreamType);
    }

    // ─── Connection Management ───

    private void AcquireQuicConnection(ConnectItem connect)
    {
        var manager = _quicManager;
        if (manager is null)
        {
            _self.Tell(new QuicTransportEvent.AcquisitionFailed(
                new InvalidOperationException("QuicConnectionManager not initialized")));
            return;
        }

        var acquireTask = manager.OpenStreamAsync(OutputStreamType.Request);
        var self = _self;

        acquireTask.ContinueWith(
            static (t, state) => ((IActorRef)state!).Tell(new QuicTransportEvent.RequestLeaseAcquired(t.Result)),
            self,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

        acquireTask.ContinueWith(
            static (t, state) => ((IActorRef)state!).Tell(new QuicTransportEvent.AcquisitionFailed(t.Exception!.GetBaseException())),
            self,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);

        var timeout = connect.Options.ConnectTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(10);
        }

        _ops.OnScheduleTimer(ConnectTimerKey, timeout);
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
        var self = _self;

        openTask.ContinueWith(
            static (t, state) => ((IActorRef)state!).Tell(new QuicTransportEvent.TypedLeaseAcquired(t.Result)),
            self,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

        openTask.ContinueWith(
            (t, state) =>
            {
                if (t.IsFaulted)
                {
                    _ops.Log.Warning("QuicConnectionStage: Failed to open {0} stream — {1}",
                        streamType, t.Exception!.GetBaseException().Message);
                }
            },
            self,
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

        if (_quicManager is { } mgr)
        {
            _ = mgr.DisposeAsync();
            _quicManager = null;
        }

        _requestHandle = null;
        _controlHandle = null;
        _encoderHandle = null;
    }

    // ─── Inbound Pumps ───

    private void StartQuicInboundPump(ConnectionHandle handle, InputStreamType streamType)
    {
        var cts = new CancellationTokenSource();
        _quicPumpCancellations.Add(cts);

        var ct = cts.Token;
        var reader = handle.InboundReader;
        var key = _currentKey;
        var self = _self;

        _ = QuicPumpAsync(reader, key, streamType, ct, self);
    }

    private static async Task QuicPumpAsync(
        ChannelReader<NetworkBuffer> reader,
        RequestEndpoint key,
        InputStreamType streamType,
        CancellationToken ct,
        IActorRef self)
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

                    self.Tell(new QuicTransportEvent.InboundData(outputItem));
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
        catch (Exception ex)
        {
            self.Tell(new QuicTransportEvent.InboundPumpFailed(ex));
            return;
        }

        // Only emit close signal for the request stream (main connection lifecycle)
        if (streamType == InputStreamType.Request)
        {
            self.Tell(new QuicTransportEvent.InboundComplete(closeKind, 0));
        }
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

    // ─── Outbound Writing ───

    private void WriteToHandle(ConnectionHandle? handle, NetworkBuffer buffer)
    {
        if (handle is null)
        {
            _ops.Log.Warning("QuicConnectionStage: Data received but no handle available — dropping element.");
            _ops.OnSignalPullInput();
            return;
        }

        var vt = handle.OutboundWriter.WriteAsync(buffer);

        if (vt.IsCompletedSuccessfully)
        {
            // Fast path: synchronous completion — no Tell overhead.
            _ops.OnSignalPullInput();
            return;
        }

        // Slow path: async completion — dispatch through StageActorRef.
        var self = _self;
        var writeTask = vt.AsTask();

        writeTask.ContinueWith(
            static (_, state) => ((IActorRef)state!).Tell(new QuicTransportEvent.OutboundWriteDone()),
            self,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

        writeTask.ContinueWith(
            static (t, state) => ((IActorRef)state!).Tell(new QuicTransportEvent.OutboundWriteFailed(t.Exception!.GetBaseException())),
            self,
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

            var self = _self;
            vt.AsTask().ContinueWith(
                static (t, state) =>
                {
                    if (t.IsFaulted)
                    {
                        ((IActorRef)state!).Tell(new QuicTransportEvent.OutboundWriteFailed(t.Exception!.GetBaseException()));
                    }
                },
                self,
                TaskContinuationOptions.ExecuteSynchronously);
        }

        _ops.OnSignalPullInput();
    }

    // ─── Lifecycle ───

    public void PostStop()
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        CleanupTransport();

        while (_pendingWrites.TryDequeue(out var orphan))
        {
            orphan.Dispose();
        }
    }
}
