namespace TurboHTTP.Tests.Protocol.Multiplexed;

using TurboHTTP.Protocol.Multiplexed;

public sealed class QuicStreamTrackerSpec
{
    [Fact(Timeout = 5000)]
    public void QuicStreamTracker_should_allocate_stream_ids_starting_at_zero_with_increment_four()
    {
        var tracker = new QuicStreamTracker();
        Assert.Equal(0L, tracker.AllocateStreamId());
        Assert.Equal(4L, tracker.AllocateStreamId());
        Assert.Equal(8L, tracker.AllocateStreamId());
        Assert.Equal(12L, tracker.AllocateStreamId());
    }

    [Fact(Timeout = 5000)]
    public void QuicStreamTracker_should_track_active_stream_count()
    {
        var tracker = new QuicStreamTracker();
        var id = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id);
        Assert.Equal(1, tracker.ActiveStreamCount);
        tracker.OnStreamClosed(id);
        Assert.Equal(0, tracker.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void QuicStreamTracker_should_enforce_concurrency_limit()
    {
        var tracker = new QuicStreamTracker(maxConcurrentStreams: 2);
        var id1 = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id1);
        var id2 = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id2);
        Assert.False(tracker.CanOpenStream());
        tracker.OnStreamClosed(id1);
        Assert.True(tracker.CanOpenStream());
    }

    [Fact(Timeout = 5000)]
    public void QuicStreamTracker_should_return_false_when_closing_unknown_stream()
    {
        var tracker = new QuicStreamTracker();
        Assert.False(tracker.OnStreamClosed(999L));
    }

    [Fact(Timeout = 5000)]
    public void QuicStreamTracker_should_reset_all_state()
    {
        var tracker = new QuicStreamTracker();
        var id = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id);
        tracker.Reset();
        Assert.Equal(0, tracker.ActiveStreamCount);
        Assert.Equal(0L, tracker.AllocateStreamId());
    }

    [Fact(Timeout = 5000)]
    public void QuicStreamTracker_should_support_custom_initial_stream_id()
    {
        var tracker = new QuicStreamTracker(initialNextStreamId: 100);
        Assert.Equal(100L, tracker.AllocateStreamId());
        Assert.Equal(104L, tracker.AllocateStreamId());
    }
}