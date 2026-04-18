using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.FrameDecoding;

public sealed class StreamTrackerResetSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public void StreamTracker_should_reset_to_initial_state()
    {
        var tracker = new StreamTracker();
        var id1 = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id1);
        var id2 = tracker.AllocateStreamId();
        tracker.OnStreamOpened(id2);
        Assert.Equal(2, tracker.ActiveStreamCount);

        tracker.Reset();

        Assert.Equal(0, tracker.ActiveStreamCount);
        Assert.Equal(1, tracker.NextStreamId);
    }
}