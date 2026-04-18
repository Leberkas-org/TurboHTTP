namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Tracks HTTP/2 stream lifecycle — ID allocation, active stream count, and concurrency limits.
/// RFC 9113 §5.1.1: Stream identifiers are odd for client-initiated, incremented by 2.
/// </summary>
internal sealed class StreamTracker
{
    private readonly HashSet<int> _activeStreamIds = [];

    public StreamTracker(int initialNextStreamId = 1, int maxConcurrentStreams = 100)
    {
        NextStreamId = initialNextStreamId;
        MaxConcurrentStreams = maxConcurrentStreams;
    }

    public int ActiveStreamCount { get; private set; }
    public int MaxConcurrentStreams { get; set; }

    /// <summary>Current next stream ID (for testing/reset visibility).</summary>
    public int NextStreamId { get; private set; }

    /// <summary>
    /// Returns true if a new stream can be opened without exceeding the concurrency limit.
    /// </summary>
    public bool CanOpenStream() => ActiveStreamCount < MaxConcurrentStreams;

    /// <summary>
    /// Resets to initial state for use on a new connection.
    /// Stream ID allocation restarts from 1; active set is cleared.
    /// </summary>
    public void Reset()
    {
        _activeStreamIds.Clear();
        ActiveStreamCount = 0;
        NextStreamId = 1;
    }

    /// <summary>
    /// Allocates the next stream ID (odd, client-initiated) and advances the counter by 2.
    /// </summary>
    public int AllocateStreamId()
    {
        var id = NextStreamId;
        NextStreamId += 2;
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