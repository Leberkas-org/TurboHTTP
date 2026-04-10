namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Tracks HTTP/2 stream lifecycle — ID allocation, active stream count, and concurrency limits.
/// RFC 9113 §5.1.1: Stream identifiers are odd for client-initiated, incremented by 2.
/// </summary>
public sealed class StreamTracker
{
    private readonly HashSet<int> _activeStreamIds = [];
    private int _nextStreamId;

    public StreamTracker(int initialNextStreamId = 1, int maxConcurrentStreams = 100)
    {
        _nextStreamId = initialNextStreamId;
        MaxConcurrentStreams = maxConcurrentStreams;
    }

    public int ActiveStreamCount { get; private set; }
    public int MaxConcurrentStreams { get; set; }

    /// <summary>
    /// Returns true if a new stream can be opened without exceeding the concurrency limit.
    /// </summary>
    public bool CanOpenStream() => ActiveStreamCount < MaxConcurrentStreams;

    /// <summary>
    /// Allocates the next stream ID (odd, client-initiated) and advances the counter by 2.
    /// </summary>
    public int AllocateStreamId()
    {
        var id = _nextStreamId;
        _nextStreamId += 2;
        return id;
    }

    /// <summary>
    /// Registers a stream as active. Call after sending HEADERS for the stream.
    /// </summary>
    public void OnStreamOpened(int streamId)
    {
        _activeStreamIds.Add(streamId);
        ActiveStreamCount++;
    }

    /// <summary>
    /// Removes a stream from the active set. Returns false if the stream was not tracked.
    /// </summary>
    public bool OnStreamClosed(int streamId)
    {
        if (!_activeStreamIds.Remove(streamId))
        {
            return false;
        }

        ActiveStreamCount--;
        return true;
    }
}
