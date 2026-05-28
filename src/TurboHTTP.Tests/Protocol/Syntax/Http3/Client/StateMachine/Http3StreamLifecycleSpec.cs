using System.Net;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3StreamLifecycleSpec
{
    private readonly FakeOps _ops = new();

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 443),
        TransportProtocol.Tcp);

    private Http3ClientStateMachine CreateMachine(FakeOps? ops = null)
        => new(new TurboClientOptions(), ops ?? _ops);

    private static void SimulateConnect(Http3ClientStateMachine sm)
        => sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void StateMachine_should_accept_request_when_connected()
    {
        var sm = CreateMachine();
        sm.PreStart();
        SimulateConnect(sm);
        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encoder_should_produce_single_headers_frame_per_request()
    {
        var tableSync = new QpackTableSync();
        var encoder = new Http3ClientEncoder(tableSync);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var frames = encoder.Encode(request);
        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8")]
    public void ErrorCode_should_define_request_cancelled()
    {
        Assert.Equal(0x10c, (int)ErrorCode.RequestCancelled);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8")]
    public void ErrorCode_should_define_request_rejected()
    {
        Assert.Equal(0x10b, (int)ErrorCode.RequestRejected);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-8")]
    public void ErrorCode_should_define_message_error()
    {
        Assert.Equal(0x10e, (int)ErrorCode.MessageError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void FrameDecoder_should_decode_data_frame_on_request_stream()
    {
        var decoder = new FrameDecoder();
        var data = new DataFrame("test"u8.ToArray());
        var result = decoder.DecodeAll(data.Serialize(), out _);
        Assert.Single(result);
        var frame = Assert.IsType<DataFrame>(result[0]);
        Assert.Equal(4, frame.Data.Length);
    }
}