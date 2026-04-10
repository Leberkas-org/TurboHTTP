namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Result of processing an inbound DATA frame through flow control.
/// </summary>
public readonly struct FlowControlResult
{
    public bool Success { get; init; }
    public bool IsConnectionViolation { get; init; }
    public bool IsStreamViolation { get; init; }
    public int ViolationStreamId { get; init; }
    public WindowUpdateFrame? ConnectionWindowUpdate { get; init; }
    public WindowUpdateFrame? StreamWindowUpdate { get; init; }
}

/// <summary>
/// Result of processing a remote SETTINGS frame.
/// </summary>
public readonly struct SettingsResult
{
    public int? MaxConcurrentStreamsChange { get; init; }
    public int? InitialWindowSizeChange { get; init; }
    public SettingsFrame AckFrame { get; init; }
}

/// <summary>
/// Connection-level RFC 9113 state: flow control (§6.9), SETTINGS (§6.5),
/// PING (§6.7), GOAWAY (§6.8), and per-stream receive windows.
/// Extracted from Http20ConnectionStage.Logic for independent testability.
/// </summary>
public sealed class ConnectionState
{
    // Per-stream receive windows (how much the server can still send per stream).
    private readonly Dictionary<int, int> _recvStreamWindows = new();
    private int _pendingConnIncrement;
    private readonly Dictionary<int, int> _pendingStreamIncrements = new();
    private readonly int _windowUpdateThreshold;

    private int _recvConnectionWindow;
    private int _sendConnectionWindow = 65535; // RFC 9113 §6.5.2 default
    private int _initialRecvStreamWindow;
    private int _initialSendStreamWindow = 65535;

    public ConnectionState(int initialRecvWindowSize = 1_048_576)
    {
        _recvConnectionWindow = initialRecvWindowSize;
        _initialRecvStreamWindow = initialRecvWindowSize;

        const int MinWindowUpdateThreshold = 8_192;
        const int MaxWindowUpdateThreshold = 262_144; // 256 KB
        _windowUpdateThreshold = Math.Max(
            MinWindowUpdateThreshold,
            Math.Min(MaxWindowUpdateThreshold, initialRecvWindowSize / 4));
    }

    public bool GoAwayReceived { get; private set; }
    public int RecvConnectionWindow => _recvConnectionWindow;
    public int SendConnectionWindow => _sendConnectionWindow;
    public int InitialSendStreamWindow => _initialSendStreamWindow;

    /// <summary>
    /// RFC 9113 §6.5: Process a remote SETTINGS frame.
    /// Returns the ACK frame and any parameter changes that need stage-level action.
    /// </summary>
    public SettingsResult OnRemoteSettings(SettingsFrame frame)
    {
        if (frame.IsAck)
        {
            return default;
        }

        int? maxConcurrentStreamsChange = null;
        int? initialWindowSizeChange = null;

        foreach (var (key, value) in frame.Parameters)
        {
            if (key == SettingsParameter.InitialWindowSize)
            {
                _initialSendStreamWindow = (int)value;
                initialWindowSizeChange = (int)value;
            }

            if (key == SettingsParameter.MaxConcurrentStreams)
            {
                maxConcurrentStreamsChange = (int)value;
            }
        }

        return new SettingsResult
        {
            MaxConcurrentStreamsChange = maxConcurrentStreamsChange,
            InitialWindowSizeChange = initialWindowSizeChange,
            AckFrame = new SettingsFrame([], isAck: true)
        };
    }

    /// <summary>
    /// RFC 9113 §6.9: Process inbound DATA frame flow control.
    /// Returns flow control violations or WINDOW_UPDATE frames to send.
    /// </summary>
    public FlowControlResult OnInboundData(int streamId, int dataLength)
    {
        _recvConnectionWindow -= dataLength;

        _recvStreamWindows.TryAdd(streamId, _initialRecvStreamWindow);
        _recvStreamWindows[streamId] -= dataLength;

        if (_recvConnectionWindow < 0)
        {
            return new FlowControlResult
            {
                Success = false,
                IsConnectionViolation = true
            };
        }

        if (_recvStreamWindows[streamId] < 0)
        {
            return new FlowControlResult
            {
                Success = false,
                IsStreamViolation = true,
                ViolationStreamId = streamId
            };
        }

        WindowUpdateFrame? connUpdate = null;
        WindowUpdateFrame? streamUpdate = null;

        if (dataLength > 0)
        {
            _pendingConnIncrement += dataLength;
            _pendingStreamIncrements.TryAdd(streamId, 0);
            _pendingStreamIncrements[streamId] += dataLength;

            if (_pendingConnIncrement >= _windowUpdateThreshold)
            {
                var increment = _pendingConnIncrement;
                _recvConnectionWindow += increment;
                connUpdate = new WindowUpdateFrame(0, increment);
                _pendingConnIncrement = 0;
            }

            if (_pendingStreamIncrements[streamId] >= _windowUpdateThreshold)
            {
                var increment = _pendingStreamIncrements[streamId];
                _recvStreamWindows[streamId] += increment;
                streamUpdate = new WindowUpdateFrame(streamId, increment);
                _pendingStreamIncrements[streamId] = 0;
            }
        }

        return new FlowControlResult
        {
            Success = true,
            ConnectionWindowUpdate = connUpdate,
            StreamWindowUpdate = streamUpdate
        };
    }

    /// <summary>
    /// RFC 9113 §6.9: Process a WINDOW_UPDATE frame from the server.
    /// </summary>
    public void OnWindowUpdate(WindowUpdateFrame frame)
    {
        if (frame.StreamId == 0)
        {
            _sendConnectionWindow += frame.Increment;
        }
    }

    /// <summary>
    /// RFC 9113 §6.7: Process a PING frame. Returns an ACK PING if needed.
    /// </summary>
    public PingFrame? OnPing(PingFrame ping)
    {
        if (!ping.IsAck)
        {
            return new PingFrame(ping.Data, true);
        }

        return null;
    }

    /// <summary>
    /// RFC 9113 §6.8: Record that a GOAWAY frame was received.
    /// </summary>
    public void OnGoAway()
    {
        GoAwayReceived = true;
    }

    /// <summary>
    /// Clean up per-stream flow control state when a stream closes.
    /// Returns a WINDOW_UPDATE frame if there was pending increment.
    /// </summary>
    public WindowUpdateFrame? OnStreamClosed(int streamId)
    {
        WindowUpdateFrame? windowUpdate = null;

        if (_pendingStreamIncrements.TryGetValue(streamId, out var pending) && pending > 0)
        {
            windowUpdate = new WindowUpdateFrame(streamId, pending);
        }

        _pendingStreamIncrements.Remove(streamId);
        _recvStreamWindows.Remove(streamId);

        return windowUpdate;
    }
}
