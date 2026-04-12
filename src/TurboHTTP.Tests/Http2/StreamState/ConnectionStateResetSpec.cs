using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.StreamState;

public sealed class ConnectionStateResetSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void ConnectionState_should_reset_goaway_flag()
    {
        var state = new ConnectionState(65535);
        state.OnGoAway();
        Assert.True(state.GoAwayReceived);

        state.Reset(65535);

        Assert.False(state.GoAwayReceived);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void ConnectionState_should_reset_send_window_to_initial()
    {
        var state = new ConnectionState(65535);
        // Simulate receiving WINDOW_UPDATE that grows window
        state.OnWindowUpdate(new WindowUpdateFrame(0, 10000));
        Assert.Equal(65535 + 10000, state.SendConnectionWindow);

        state.Reset(65535);

        Assert.Equal(65535, state.SendConnectionWindow);
    }
}
