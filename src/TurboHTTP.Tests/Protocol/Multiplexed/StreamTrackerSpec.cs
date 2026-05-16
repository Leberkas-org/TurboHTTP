using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Multiplexed;

public sealed class StreamTrackerSpec
{
    [Fact(Timeout = 5000)]
    public void StreamTracker_should_allocate_odd_stream_ids_starting_at_one()
    {
        var tracker = new StreamTracker();
        Assert.Equal(1, tracker.AllocateStreamId());
        Assert.Equal(3, tracker.AllocateStreamId());
        Assert.Equal(5, tracker.AllocateStreamId());
    }

    [Fact(Timeout = 5000)]
    public void StreamTracker_should_track_active_stream_count()
    {
        var tracker = new StreamTracker();
        var id = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id);
        Assert.Equal(1, tracker.ActiveStreamCount);
        tracker.OnStreamClosed(id);
        Assert.Equal(0, tracker.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void StreamTracker_should_enforce_concurrency_limit()
    {
        var tracker = new StreamTracker(maxConcurrentStreams: 2);
        var id1 = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id1);
        var id2 = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id2);
        Assert.False(tracker.CanOpenStream());
        tracker.OnStreamClosed(id1);
        Assert.True(tracker.CanOpenStream());
    }

    [Fact(Timeout = 5000)]
    public void StreamTracker_should_return_false_when_closing_unknown_stream()
    {
        var tracker = new StreamTracker();
        Assert.False(tracker.OnStreamClosed(999));
    }

    [Fact(Timeout = 5000)]
    public void StreamTracker_should_reset_all_state()
    {
        var tracker = new StreamTracker();
        var id = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id);
        tracker.Reset();
        Assert.Equal(0, tracker.ActiveStreamCount);
        Assert.Equal(1, tracker.AllocateStreamId());
    }
}