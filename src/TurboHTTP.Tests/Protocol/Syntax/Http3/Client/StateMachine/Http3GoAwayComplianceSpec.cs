using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Tests.Shared;
using System.Net;
using TurboHTTP.Client;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3GoAwayComplianceSpec
{
    private readonly FakeOps _ops = new();

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 443),
        TransportProtocol.Tcp);

    private Http3ClientStateMachine CreateMachine(FakeOps? ops = null)
        => new(new TurboClientOptions(), ops ?? _ops);

    private static TransportBuffer SerializeFrame(Http3Frame frame)
    {
        var buffer = TransportBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void StateMachine_should_not_accept_requests_after_goaway()
    {
        var sm = CreateMachine();
        sm.PreStart();
        sm.DecodeServerData(new MultiplexedData(SerializeFrame(new GoAwayFrame(0)), -2));
        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void ConnectionState_should_set_goaway_received()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        state.OnServerGoAway(new GoAwayFrame(4));
        Assert.True(state.GoAwayReceived);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void ConnectionState_should_reject_goaway_with_invalid_stream_id()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        Assert.Throws<HttpProtocolException>(() => state.OnServerGoAway(new GoAwayFrame(3)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void ConnectionState_should_accept_decreasing_goaway_stream_id()
    {
        var state = new ConnectionState(TimeSpan.FromSeconds(30));
        state.OnServerGoAway(new GoAwayFrame(8));
        state.OnServerGoAway(new GoAwayFrame(4));
        Assert.True(state.GoAwayReceived);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5")]
    public void ConnectionState_should_track_idle_timeout()
    {
        var state = new ConnectionState(TimeSpan.FromMilliseconds(100));
        state.RecordActivity();
        Assert.False(state.IsIdleTimeoutExpired());
    }
}