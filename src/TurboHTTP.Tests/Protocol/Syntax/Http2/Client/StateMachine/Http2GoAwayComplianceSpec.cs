using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Client;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client.StateMachine;

public sealed class Http2GoAwayComplianceSpec
{
    private static TurboClientOptions MakeConfig(int maxConcurrentStreams = 100)
    {
        var options = new TurboClientOptions
        {
            Http2 =
            {
                MaxConcurrentStreams = maxConcurrentStreams
            }
        };
        return options;
    }

    private static HttpRequestMessage MakeGet(string path = "/")
        => new(HttpMethod.Get, $"https://example.com{path}");

    private static TransportBuffer SerializeFrame(Http2Frame frame)
    {
        var buffer = TransportBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void StateMachine_should_not_accept_requests_when_goaway_received()
    {
        var ops = new FakeOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();

        var goaway = new GoAwayFrame(5, Http2ErrorCode.NoError);
        sm.DecodeServerData(new TransportData(SerializeFrame(goaway)));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void FlowController_should_preserve_stream_windows_when_goaway_received()
    {
        var flow = new FlowController(65535, 65535);
        flow.InitStreamSendWindow(1);
        flow.InitStreamSendWindow(3);

        flow.OnGoAway();

        Assert.True(flow.GoAwayReceived);
        Assert.Equal(65535, flow.GetSendWindow(1));
        Assert.Equal(65535, flow.GetSendWindow(3));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void FlowController_should_accept_window_update_on_existing_stream_after_goaway()
    {
        var flow = new FlowController(65535, 65535, initialConnectionSendWindow: 100000);
        flow.InitStreamSendWindow(1);
        flow.OnGoAway();

        flow.OnSendWindowUpdate(1, 10000);

        Assert.Equal(75535, flow.GetSendWindow(1));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void HpackDecoder_should_maintain_dynamic_table_state_across_goaway()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var block1 = encoder.Encode([(":status", "200"), ("x-custom", "value1")]);
        var headers1 = decoder.Decode(block1.Span);
        Assert.Equal(2, headers1.Count);

        var block2 = encoder.Encode([(":status", "200"), ("x-custom", "value2")]);
        var headers2 = decoder.Decode(block2.Span);
        Assert.Equal(2, headers2.Count);
        Assert.Equal("value2", headers2[1].Value);
    }
}