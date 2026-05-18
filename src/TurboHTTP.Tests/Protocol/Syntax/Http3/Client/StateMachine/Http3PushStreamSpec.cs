using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3PushStreamSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.6")]
    public void ConnectionState_should_track_push_count()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30), maxPushCount: 5);
        state.RecordPush();
        state.RecordPush();
        // Should not throw for pushes within limit
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.6")]
    public void ConnectionState_should_reject_push_exceeding_max()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30), maxPushCount: 2);
        state.RecordPush();
        state.RecordPush();
        Assert.Throws<HttpProtocolException>(() => state.RecordPush());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.6")]
    public void ConnectionState_should_track_cancelled_push_ids()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        var cancelFrame = new CancelPushFrame(42);
        state.OnReceivedCancelPush(cancelFrame);
        Assert.True(state.IsPushCancelled(42));
        Assert.False(state.IsPushCancelled(43));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.6")]
    public void MaxPushIdFrame_should_serialize_and_decode()
    {
        var decoder = new FrameDecoder();
        var frame = new MaxPushIdFrame(100);
        var result = decoder.DecodeAll(frame.Serialize(), out _);
        Assert.Single(result);
        var decoded = Assert.IsType<MaxPushIdFrame>(result[0]);
        Assert.Equal(100, decoded.PushId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.6")]
    public void CancelPushFrame_should_serialize_and_decode()
    {
        var decoder = new FrameDecoder();
        var frame = new CancelPushFrame(7);
        var result = decoder.DecodeAll(frame.Serialize(), out _);
        Assert.Single(result);
        var decoded = Assert.IsType<CancelPushFrame>(result[0]);
        Assert.Equal(7, decoded.PushId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.6")]
    public void PushPromiseFrame_should_serialize_and_decode()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var block = encoder.Encode([(":status", "200"), (":method", "GET")]);
        var decoder = new FrameDecoder();
        var frame = new PushPromiseFrame(1, block);
        var result = decoder.DecodeAll(frame.Serialize(), out _);
        Assert.Single(result);
        var decoded = Assert.IsType<PushPromiseFrame>(result[0]);
        Assert.Equal(1, decoded.PushId);
    }
}
