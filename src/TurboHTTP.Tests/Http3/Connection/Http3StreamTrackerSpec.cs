using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class Http3StreamTrackerSpec
{
    [Fact(Timeout = 5000)]
    public void AllocateStreamId_should_return_zero_for_first_allocation()
    {
        var tracker = new StreamTracker();

        var id = tracker.AllocateStreamId();

        Assert.Equal(0L, id);
    }

    [Fact(Timeout = 5000)]
    public void AllocateStreamId_should_increment_by_four()
    {
        var tracker = new StreamTracker();

        var first = tracker.AllocateStreamId();
        var second = tracker.AllocateStreamId();
        var third = tracker.AllocateStreamId();

        Assert.Equal(0L, first);
        Assert.Equal(4L, second);
        Assert.Equal(8L, third);
    }

    [Fact(Timeout = 5000)]
    public void AllocateStreamId_should_use_custom_initial_id()
    {
        var tracker = new StreamTracker(initialNextStreamId: 12);

        var id = tracker.AllocateStreamId();

        Assert.Equal(12L, id);
        Assert.Equal(16L, tracker.NextStreamId);
    }

    [Fact(Timeout = 5000)]
    public void NextStreamId_should_reflect_current_counter()
    {
        var tracker = new StreamTracker();

        Assert.Equal(0L, tracker.NextStreamId);
        tracker.AllocateStreamId();
        Assert.Equal(4L, tracker.NextStreamId);
        tracker.AllocateStreamId();
        Assert.Equal(8L, tracker.NextStreamId);
    }

    [Fact(Timeout = 5000)]
    public void CanOpenStream_should_return_true_when_below_limit()
    {
        var tracker = new StreamTracker(maxConcurrentStreams: 2);

        Assert.True(tracker.CanOpenStream());
    }

    [Fact(Timeout = 5000)]
    public void CanOpenStream_should_return_false_when_at_limit()
    {
        var tracker = new StreamTracker(maxConcurrentStreams: 2);
        tracker.OnStreamOpened(0);
        tracker.OnStreamOpened(4);

        Assert.False(tracker.CanOpenStream());
    }

    [Fact(Timeout = 5000)]
    public void CanOpenStream_should_return_true_after_stream_closed()
    {
        var tracker = new StreamTracker(maxConcurrentStreams: 1);
        tracker.OnStreamOpened(0);

        Assert.False(tracker.CanOpenStream());

        tracker.OnStreamClosed(0);

        Assert.True(tracker.CanOpenStream());
    }

    [Fact(Timeout = 5000)]
    public void OnStreamOpened_should_increment_active_count()
    {
        var tracker = new StreamTracker();

        Assert.Equal(0, tracker.ActiveStreamCount);
        tracker.OnStreamOpened(0);
        Assert.Equal(1, tracker.ActiveStreamCount);
        tracker.OnStreamOpened(4);
        Assert.Equal(2, tracker.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void OnStreamClosed_should_decrement_active_count()
    {
        var tracker = new StreamTracker();
        tracker.OnStreamOpened(0);
        tracker.OnStreamOpened(4);

        tracker.OnStreamClosed(0);

        Assert.Equal(1, tracker.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void OnStreamClosed_should_return_false_for_unknown_stream()
    {
        var tracker = new StreamTracker();

        var result = tracker.OnStreamClosed(99);

        Assert.False(result);
        Assert.Equal(0, tracker.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void OnStreamClosed_should_return_true_for_tracked_stream()
    {
        var tracker = new StreamTracker();
        tracker.OnStreamOpened(0);

        var result = tracker.OnStreamClosed(0);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_active_streams()
    {
        var tracker = new StreamTracker();
        tracker.OnStreamOpened(0);
        tracker.OnStreamOpened(4);

        tracker.Reset();

        Assert.Equal(0, tracker.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_restart_stream_id_allocation_from_zero()
    {
        var tracker = new StreamTracker();
        tracker.AllocateStreamId(); // 0
        tracker.AllocateStreamId(); // 4

        tracker.Reset();

        Assert.Equal(0L, tracker.NextStreamId);
        Assert.Equal(0L, tracker.AllocateStreamId());
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrentStreams_should_be_settable()
    {
        var tracker = new StreamTracker(maxConcurrentStreams: 1);
        tracker.OnStreamOpened(0);

        Assert.False(tracker.CanOpenStream());

        tracker.MaxConcurrentStreams = 2;

        Assert.True(tracker.CanOpenStream());
    }

    [Fact(Timeout = 5000)]
    public void StreamIds_should_support_large_values()
    {
        // QUIC uses 62-bit variable-length integers — verify long works for large IDs
        var tracker = new StreamTracker(initialNextStreamId: 4_611_686_018_427_387_900L);

        var id = tracker.AllocateStreamId();

        Assert.Equal(4_611_686_018_427_387_900L, id);
        tracker.OnStreamOpened(id);
        Assert.Equal(1, tracker.ActiveStreamCount);
        Assert.True(tracker.OnStreamClosed(id));
    }
}