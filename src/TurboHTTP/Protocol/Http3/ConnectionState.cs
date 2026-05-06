namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Encapsulates all HTTP/3 connection-level state in a single class.
/// Manages GoAway, Settings, idle timeout, and push state.
/// </summary>
internal sealed class ConnectionState
{
    private readonly TimeSpan _idleTimeout;

    public bool GoAwayReceived { get; set; }
    public long LastGoAwayStreamId { get; private set; } = -1;
    public bool RemoteSettingsReceived { get; private set; }
    public Settings? RemoteSettings { get; private set; }
    public long? RemoteMaxFieldSectionSize => RemoteSettings?.MaxFieldSectionSize;

    private long _lastActivity;

    public int ActiveStreamCount { get; private set; }
    public bool IsTimeoutDisabled => _idleTimeout == TimeSpan.Zero;
    public long MaxPushId { get; set; }

    private readonly HashSet<long> _cancelledPushIds = [];
    private int _pushCount;
    private readonly int _maxPushCount;

    public ConnectionState(TimeSpan idleTimeout, int maxPushCount = 0)
    {
        _idleTimeout = idleTimeout;
        _maxPushCount = maxPushCount;
        _lastActivity = Environment.TickCount64;
    }

    public void OnServerGoAway(GoAwayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var streamId = frame.StreamId;

        if (streamId % 4 != 0)
        {
            throw new Http3Exception(
                ErrorCode.IdError,
                $"Server GOAWAY stream ID {streamId} is not a valid client-initiated bidirectional stream ID (must be divisible by 4, RFC 9114 §5.2).");
        }

        if (LastGoAwayStreamId >= 0 && streamId > LastGoAwayStreamId)
        {
            throw new Http3Exception(
                ErrorCode.IdError,
                $"Server GOAWAY stream ID {streamId} must not increase beyond previous value {LastGoAwayStreamId} (RFC 9114 §5.2).");
        }

        LastGoAwayStreamId = streamId;
        GoAwayReceived = true;
    }

    public void OnRemoteSettings(SettingsFrame settingsFrame)
    {
        if (RemoteSettingsReceived)
        {
            throw new Http3Exception(
                ErrorCode.FrameUnexpected,
                "A second SETTINGS frame on the control stream is a connection error (RFC 9114 §7.2.4).");
        }

        var settings = new Settings();
        foreach (var (id, val) in settingsFrame.Parameters)
        {
            settings.Set(id, val);
        }

        RemoteSettings = settings;
        RemoteSettingsReceived = true;
    }

    public void RecordActivity()
    {
        _lastActivity = Environment.TickCount64;
    }

    public void OnStreamOpened()
    {
        ActiveStreamCount++;
        RecordActivity();
    }

    public void OnStreamClosed()
    {
        if (ActiveStreamCount > 0)
        {
            ActiveStreamCount--;
        }

        RecordActivity();
    }

    public bool IsIdleTimeoutExpired()
    {
        if (IsTimeoutDisabled)
        {
            return false;
        }

        return Environment.TickCount64 - _lastActivity >= (long)_idleTimeout.TotalMilliseconds;
    }

    public TimeSpan TimeUntilExpiry()
    {
        if (IsTimeoutDisabled)
        {
            return TimeSpan.MaxValue;
        }

        var remainingMs = (long)_idleTimeout.TotalMilliseconds - (Environment.TickCount64 - _lastActivity);
        return remainingMs > 0 ? TimeSpan.FromMilliseconds(remainingMs) : TimeSpan.Zero;
    }

    public static TimeSpan ComputeEffectiveTimeout(TimeSpan localTimeout, TimeSpan remoteTimeout)
    {
        if (localTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(localTimeout), localTimeout,
                "Timeout must be non-negative.");
        }

        if (remoteTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(remoteTimeout), remoteTimeout,
                "Timeout must be non-negative.");
        }

        if (localTimeout == TimeSpan.Zero)
        {
            return remoteTimeout;
        }

        if (remoteTimeout == TimeSpan.Zero)
        {
            return localTimeout;
        }

        return localTimeout < remoteTimeout ? localTimeout : remoteTimeout;
    }

    public void RecordPush()
    {
        if (_pushCount >= _maxPushCount)
        {
            throw new Http3Exception(
                ErrorCode.ExcessiveLoad,
                $"Server exceeded push limit of {_maxPushCount} push promises (RFC 9114 §10.5).");
        }

        _pushCount++;
    }

    public void OnReceivedCancelPush(CancelPushFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _cancelledPushIds.Add(frame.PushId);
    }

    public bool IsPushCancelled(long pushId) => _cancelledPushIds.Contains(pushId);

    public void Reset()
    {
        GoAwayReceived = false;
        LastGoAwayStreamId = -1;
        RemoteSettingsReceived = false;
        RemoteSettings = null;
        ActiveStreamCount = 0;
        _pushCount = 0;
        _cancelledPushIds.Clear();
        MaxPushId = 0;
        RecordActivity();
    }
}