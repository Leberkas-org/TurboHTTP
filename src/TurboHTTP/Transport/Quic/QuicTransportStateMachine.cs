using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Tcp;

// QUIC APIs are platform-guarded; usage is gated at runtime via ConnectItem.Options being QuicOptions.
#pragma warning disable CA1416

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Encapsulates all QUIC transport state and logic — multi-stream I/O (request, control, encoder),
/// tagged item routing, and connection lifecycle management.
/// Calls back into <see cref="ITransportOperations"/> for Akka-specific operations
/// (Push, Pull, Timer, Complete, Fail).
/// Async events arrive via <see cref="Dispatch"/> after being marshaled through the StageActorRef.
/// <para>
/// Connection acquisition is delegated to <see cref="QuicConnectionManagerActor"/> (via actor tell),
/// mirroring how <see cref="TcpTransportStateMachine"/> uses <see cref="TcpConnectionManagerActor"/>.
/// </para>
/// </summary>
internal sealed class QuicTransportStateMachine
{
    private const string ConnectTimerKey = "connect-timeout";

    private readonly ITransportOperations _ops;
    private readonly IActorRef _self;
    private readonly IActorRef _quicManagerActor;

    private int _connectionGen;

    private QuicConnectionLease? _currentConnectionLease;
    private ConnectionHandle? _requestHandle;
    private ConnectionHandle? _controlHandle;
    private ConnectionHandle? _encoderHandle;

    private TlsCloseKind _lastCloseKind = TlsCloseKind.CleanClose;

    /// <summary>Pending control items buffered before control stream is ready.</summary>
    private readonly Queue<NetworkBuffer> _pendingControlItems = new();

    /// <summary>Pending QPACK encoder items buffered before encoder stream is ready.</summary>
    private readonly Queue<NetworkBuffer> _pendingEncoderItems = new();

    /// <summary>All active stream leases for this connection (disposed on Cleanup).</summary>
    private readonly List<ConnectionLease> _activeLeases = [];

    /// <summary>CancellationTokenSources for all active QUIC inbound stream pumps.</summary>
    private readonly List<CancellationTokenSource> _quicPumpCancellations = [];

    private RequestEndpoint _currentKey;
    private ConnectItem? _pendingConnect;
    private CancellationTokenSource? _acquireCts;
    private CancellationTokenSource? _inboundAcceptCts;

    /// <summary>NetworkBuffers buffered before the request handle is available.</summary>
    private readonly Queue<NetworkBuffer> _pendingWrites = new();

    public QuicTransportStateMachine(ITransportOperations ops, IActorRef self, IActorRef quicManagerActor)
    {
        _ops = ops;
        _self = self;
        _quicManagerActor = quicManagerActor;
    }

    public void Dispatch(QuicTransportEvent evt)
    {
        switch (evt)
        {
            case QuicTransportEvent.ConnectionLeaseAcquired e:
                OnConnectionLeaseAcquired(e.Lease);
                break;
            case QuicTransportEvent.RequestLeaseAcquired e:
                OnRequestLeaseAcquired(e.Lease);
                break;
            case QuicTransportEvent.TypedLeaseAcquired e:
                OnTypedLeaseAcquired(e.Lease, e.StreamType);
                break;
            case QuicTransportEvent.AcquisitionFailed e:
                OnAcquisitionFailed(e.Error);
                break;
            case QuicTransportEvent.InboundData e:
                if (e.Gen == _connectionGen)
                {
                    _ops.OnPushOutput(e.Item);
                }

                break;
            case QuicTransportEvent.InboundComplete e:
                if (e.Gen == _connectionGen)
                {
                    OnInboundComplete(e.CloseKind);
                }

                break;
            case QuicTransportEvent.InboundPumpFailed e:
                _ops.Log.Warning("QuicConnectionStage: Inbound pump failed — {0}", e.Error.Message);
                OnInboundComplete(TlsCloseKind.AbruptClose);
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

        AcquireQuicConnection(quicOptions, connect);
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

    private void OnConnectionLeaseAcquired(QuicConnectionLease lease)
    {
        _currentConnectionLease = lease;

        // Open the request stream on the now-pooled connection
        _ = lease.Handle.OpenStreamAsLeaseAsync(OutputStreamType.Request)
            .PipeTo(_self,
                success: streamLease => new QuicTransportEvent.RequestLeaseAcquired(streamLease),
                failure: ex => new QuicTransportEvent.AcquisitionFailed(ex.GetBaseException()));
    }

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

        // Start accepting server-initiated inbound streams
        StartQuicInboundAcceptLoop();

        // Flush any NetworkBuffers buffered before the request handle was available
        while (_pendingWrites.TryDequeue(out var buffered))
        {
            WriteToHandle(_requestHandle, buffered);
        }

        _ops.OnSignalPullInput();
    }

    private void OnTypedLeaseAcquired(ConnectionLease lease, OutputStreamType streamType)
    {
        _activeLeases.Add(lease);

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
        _lastCloseKind = closeKind;

        var signal = new CloseSignalItem(closeKind) { Key = _currentKey };
        _ops.OnPushOutput(signal);

        _requestHandle = null;
        _controlHandle = null;
        _encoderHandle = null;
    }

    private void OnInboundStreamReady(QuicConnectionHandle.InboundStream inbound)
    {
        _activeLeases.Add(inbound.Lease);
        StartQuicInboundPump(inbound.Lease.Handle, inbound.StreamType);
    }

    private void AcquireQuicConnection(QuicOptions options, ConnectItem connect)
    {
        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = new CancellationTokenSource();

        var acquireTask = QuicConnectionManagerActor.AcquireAsync(
            _quicManagerActor, options, connect.Key, _acquireCts.Token);

        acquireTask.PipeTo(_self, _self,
            connLease => new QuicTransportEvent.ConnectionLeaseAcquired(connLease),
            ex => new QuicTransportEvent.AcquisitionFailed(ex.GetBaseException()));

        var timeout = connect.Options.ConnectTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(10);
        }

        _ops.OnScheduleTimer(ConnectTimerKey, timeout);
    }

    private void OpenTypedStream(OutputStreamType streamType)
    {
        if (_currentConnectionLease is null)
        {
            return;
        }

        _ = _currentConnectionLease.Handle.OpenStreamAsLeaseAsync(streamType)
            .PipeTo(_self,
                success: lease => new QuicTransportEvent.TypedLeaseAcquired(lease, streamType),
                failure: ex =>
                {
                    _ops.Log.Warning("QuicConnectionStage: Failed to open {0} stream — {1}",
                        streamType, ex.GetBaseException().Message);
                    return new QuicTransportEvent.AcquisitionFailed(ex.GetBaseException());
                });
    }

    private void ReturnConnectionToPool(bool canReuse)
    {
        if (_currentConnectionLease is null)
        {
            return;
        }

        var lease = _currentConnectionLease;
        _currentConnectionLease = null;
        _quicManagerActor.Tell(new QuicConnectionManagerActor.Release(lease, canReuse));
    }

    private void CleanupTransport()
    {
        _connectionGen++;

        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = null;

        StopAllQuicPumps();

        foreach (var lease in _activeLeases)
        {
            lease.Dispose();
        }

        _activeLeases.Clear();

        ReturnConnectionToPool(_lastCloseKind == TlsCloseKind.CleanClose);
        _lastCloseKind = TlsCloseKind.CleanClose;

        _requestHandle = null;
        _controlHandle = null;
        _encoderHandle = null;
    }

    private void StartQuicInboundAcceptLoop()
    {
        if (_currentConnectionLease is null)
        {
            return;
        }

        _inboundAcceptCts?.Cancel();
        _inboundAcceptCts?.Dispose();
        _inboundAcceptCts = new CancellationTokenSource();

        var handle = _currentConnectionLease.Handle;
        var self = _self;
        _ = QuicInboundAcceptLoopAsync(handle, self, _inboundAcceptCts.Token);
    }

    private static async Task QuicInboundAcceptLoopAsync(QuicConnectionHandle handle, IActorRef self,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var inbound = await handle.AcceptInboundStreamAsLeaseAsync(ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                inbound?.Lease.Dispose();
                return;
            }

            if (inbound is null)
            {
                continue; // unknown stream type or transient error — try again
            }

            self.Tell(new QuicTransportEvent.InboundStreamReady(inbound));
        }
    }

    private void StartQuicInboundPump(ConnectionHandle handle, InputStreamType streamType)
    {
        var cts = new CancellationTokenSource();
        _quicPumpCancellations.Add(cts);

        var ct = cts.Token;
        var reader = handle.InboundReader;
        var key = _currentKey;
        var self = _self;
        var gen = _connectionGen;

        _ = QuicPumpAsync(reader, key, streamType, ct, self, gen);
    }

    private static async Task QuicPumpAsync(
        ChannelReader<NetworkBuffer> reader,
        RequestEndpoint key,
        InputStreamType streamType,
        CancellationToken ct,
        IActorRef self,
        int gen)
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

                    self.Tell(new QuicTransportEvent.InboundData(outputItem, gen));
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
            self.Tell(new QuicTransportEvent.InboundComplete(closeKind, gen));
        }
    }

    private void StopAllQuicPumps()
    {
        _inboundAcceptCts?.Cancel();
        _inboundAcceptCts?.Dispose();
        _inboundAcceptCts = null;

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
            _ops.Log.Warning("QuicConnectionStage: Data received but no handle available — dropping element.");
            _ops.OnSignalPullInput();
            return;
        }

        _ = handle.OutboundWriter.WriteAsync(buffer)
            .PipeTo(_self,
                success: () => new QuicTransportEvent.OutboundWriteDone(),
                failure: ex => new QuicTransportEvent.OutboundWriteFailed(ex.GetBaseException()));
    }

    private void FlushPendingQuicItems(
        Queue<NetworkBuffer> pending,
        ConnectionHandle handle)
    {
        while (pending.TryDequeue(out var item))
        {
            _ = handle.OutboundWriter.WriteAsync(item)
                .PipeTo(_self,
                    success: () => new QuicTransportEvent.OutboundWriteDone(),
                    failure: ex => new QuicTransportEvent.OutboundWriteFailed(ex.GetBaseException()));
        }

        _ops.OnSignalPullInput();
    }

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