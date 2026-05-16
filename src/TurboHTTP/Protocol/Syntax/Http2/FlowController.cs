using TurboHTTP.Protocol.Multiplexed;

namespace TurboHTTP.Protocol.Syntax.Http2;

internal sealed class FlowController : IFlowController<int>
{
    private readonly Dictionary<int, int> _recvStreamWindows = new();
    private int _pendingConnIncrement;
    private readonly Dictionary<int, int> _pendingStreamIncrements = new();
    private int _windowUpdateThreshold;

    private int _recvConnectionWindow;
    private int _initialRecvStreamWindow;

    private long _connectionSendWindow;
    private long _initialSendStreamWindow;
    private readonly Dictionary<int, long> _streamSendWindows = new();

    public FlowController(
        int connectionWindowSize,
        int streamWindowSize,
        long initialConnectionSendWindow = 65535,
        long initialStreamSendWindow = 65535)
    {
        _recvConnectionWindow = connectionWindowSize;
        _initialRecvStreamWindow = streamWindowSize;
        _connectionSendWindow = initialConnectionSendWindow;
        _initialSendStreamWindow = initialStreamSendWindow;

        const int minWindowUpdateThreshold = 8_192;
        _windowUpdateThreshold = Math.Max(minWindowUpdateThreshold, streamWindowSize / 2);
    }

    public bool GoAwayReceived { get; private set; }

    public long GetSendWindow(int streamId)
    {
        var streamWindow = _streamSendWindows.GetValueOrDefault(streamId, _initialSendStreamWindow);
        return Math.Max(0L, Math.Min(_connectionSendWindow, streamWindow));
    }

    public void OnDataSent(int streamId, int length)
    {
        _connectionSendWindow -= length;
        if (_streamSendWindows.TryGetValue(streamId, out var current))
        {
            _streamSendWindows[streamId] = current - length;
        }
    }

    public void OnSendWindowUpdate(int streamId, int increment)
    {
        if (streamId == 0)
        {
            _connectionSendWindow += increment;
        }
        else
        {
            var current = _streamSendWindows.GetValueOrDefault(streamId, _initialSendStreamWindow);
            _streamSendWindows[streamId] = current + increment;
        }
    }

    public FlowControlResult<int> OnInboundData(int streamId, int dataLength)
    {
        _recvConnectionWindow -= dataLength;

        _recvStreamWindows.TryAdd(streamId, _initialRecvStreamWindow);
        _recvStreamWindows[streamId] -= dataLength;

        if (_recvConnectionWindow < 0)
        {
            return new FlowControlResult<int> { Success = false, IsConnectionViolation = true };
        }

        if (_recvStreamWindows[streamId] < 0)
        {
            return new FlowControlResult<int>
            {
                Success = false,
                IsStreamViolation = true,
                ViolationStreamId = streamId
            };
        }

        WindowUpdateSignal<int>? connUpdate = null;
        WindowUpdateSignal<int>? streamUpdate = null;

        if (dataLength > 0)
        {
            _pendingConnIncrement += dataLength;
            _pendingStreamIncrements.TryAdd(streamId, 0);
            _pendingStreamIncrements[streamId] += dataLength;

            if (_pendingConnIncrement >= _windowUpdateThreshold)
            {
                var increment = _pendingConnIncrement;
                _recvConnectionWindow += increment;
                connUpdate = new WindowUpdateSignal<int>(0, increment);
                _pendingConnIncrement = 0;
            }

            if (_pendingStreamIncrements[streamId] >= _windowUpdateThreshold)
            {
                var increment = _pendingStreamIncrements[streamId];
                _recvStreamWindows[streamId] += increment;
                streamUpdate = new WindowUpdateSignal<int>(streamId, increment);
                _pendingStreamIncrements[streamId] = 0;
            }
        }

        return new FlowControlResult<int>
        {
            Success = true,
            ConnectionWindowUpdate = connUpdate,
            StreamWindowUpdate = streamUpdate
        };
    }

    public void InitStreamSendWindow(int streamId)
    {
        _streamSendWindows[streamId] = _initialSendStreamWindow;
    }

    public void RemoveStreamSendWindow(int streamId)
    {
        _streamSendWindows.Remove(streamId);
    }

    public void ApplyInitialWindowSizeDelta(long delta)
    {
        _initialSendStreamWindow += delta;
        foreach (var streamId in _streamSendWindows.Keys.ToList())
        {
            _streamSendWindows[streamId] += delta;
        }
    }

    public WindowUpdateSignal<int>? OnStreamClosed(int streamId)
    {
        WindowUpdateSignal<int>? signal = null;

        if (_pendingStreamIncrements.TryGetValue(streamId, out var pending) && pending > 0)
        {
            signal = new WindowUpdateSignal<int>(streamId, pending);
        }

        _pendingStreamIncrements.Remove(streamId);
        _recvStreamWindows.Remove(streamId);
        _streamSendWindows.Remove(streamId);

        return signal;
    }

    public void OnGoAway()
    {
        GoAwayReceived = true;
    }

    public void Reset(int connectionWindowSize, int streamWindowSize)
    {
        GoAwayReceived = false;
        _recvConnectionWindow = connectionWindowSize;
        _initialRecvStreamWindow = streamWindowSize;
        _connectionSendWindow = 65535;
        _initialSendStreamWindow = 65535;
        _recvStreamWindows.Clear();
        _streamSendWindows.Clear();
        _pendingConnIncrement = 0;
        _pendingStreamIncrements.Clear();

        const int minWindowUpdateThreshold = 8_192;
        _windowUpdateThreshold = Math.Max(minWindowUpdateThreshold, streamWindowSize / 2);
    }

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
                initialWindowSizeChange = (int)value;
                ApplyInitialWindowSizeDelta((int)value - (int)_initialSendStreamWindow);
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

    public PingFrame? OnPing(PingFrame ping)
    {
        if (!ping.IsAck)
        {
            return new PingFrame(ping.Data, true);
        }

        return null;
    }
}