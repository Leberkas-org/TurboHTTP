using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;
using static TurboHTTP.StreamTests.Http2.Http2ConnectionTestHelper;

namespace TurboHTTP.StreamTests.Http2;

public sealed class Http2ConnectionPingSpec : StreamTestBase
{
    private async Task<(IReadOnlyList<HttpResponseMessage> Downstream, IReadOnlyList<Http2Frame> ServerBound,
        IReadOnlyList<ITransportOutbound> Signals)> RunAsync(
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var networkSink = Sink.Seq<ITransportOutbound>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, networkSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, nwSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage(new TurboClientOptions
                    { Http2 = { InitialConnectionWindowSize = 65535 } }));
                    var serverSource = b.Add(Source.From(FramesToInputs(serverFrames)));
                    var requestSource = b.Add(Source.Never<HttpRequestMessage>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var mat = graph.Run(Materializer);
        var (downstreamTask, networkTask) = (mat.m1, mat.m2);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var networkItems = await networkTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (downstream, DecodeFrames(networkItems, skipPreface: true), ExtractSignals(networkItems));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.7")]
    public async Task Http2ConnectionPing_should_send_ack_response_when_ping_without_ack_received()
    {
        var ping = new PingFrame(new byte[8], isAck: false);

        var (_, serverBound, _) = await RunAsync(ping);

        var response = Assert.Single(serverBound);
        var pingAck = Assert.IsType<PingFrame>(response);
        Assert.True(pingAck.IsAck, "Response must be a PING with ACK flag set");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.7")]
    public async Task Http2ConnectionPing_should_echo_identical_payload_in_ping_ack()
    {
        var payload = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF };
        var ping = new PingFrame(payload, isAck: false);

        var (_, serverBound, _) = await RunAsync(ping);

        var pingAck = Assert.IsType<PingFrame>(Assert.Single(serverBound));
        Assert.True(pingAck.IsAck);
        Assert.True(pingAck.Data.Span.SequenceEqual(payload));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.7")]
    public async Task Http2ConnectionPing_should_not_trigger_response_when_ping_with_ack_received()
    {
        var pingAck = new PingFrame(new byte[8], isAck: true);

        var (_, serverBound, _) = await RunAsync(pingAck);

        Assert.Empty(serverBound);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.7")]
    public async Task Http2ConnectionPing_should_send_ping_response_on_stream_zero()
    {
        var ping = new PingFrame(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA, 0xF9, 0xF8 }, isAck: false);

        var (_, serverBound, _) = await RunAsync(ping);

        var pingAck = Assert.IsType<PingFrame>(Assert.Single(serverBound));
        Assert.Equal(0, pingAck.StreamId);
        Assert.True(pingAck.IsAck);
    }
}


