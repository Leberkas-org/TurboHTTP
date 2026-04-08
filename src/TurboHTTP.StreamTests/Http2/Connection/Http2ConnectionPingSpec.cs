using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http2.Connection;

/// <summary>
/// Tests PING frame handling in the HTTP/2 connection stage per RFC 9113.
/// Verifies that PING frames from the server receive a PING ACK response and that initiation is handled correctly.
/// </summary>
[Trait("RFC", "RFC9113-6.7")]
public sealed class Http2ConnectionPingSpec : StreamTestBase
{
    private async Task<(IReadOnlyList<HttpResponseMessage> Downstream, IReadOnlyList<Http2Frame> ServerBound)> RunAsync(
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<HttpResponseMessage>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<HttpRequestMessage>());
                    var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (downstream, serverBound);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionPing_should_send_ack_response_when_ping_without_ack_received()
    {
        var ping = new PingFrame(new byte[8], isAck: false);

        var (_, serverBound) = await RunAsync(ping);

        var response = Assert.Single(serverBound);
        var pingAck = Assert.IsType<PingFrame>(response);
        Assert.True(pingAck.IsAck, "Response must be a PING with ACK flag set");
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionPing_should_echo_identical_payload_in_ping_ack()
    {
        var payload = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF };
        var ping = new PingFrame(payload, isAck: false);

        var (_, serverBound) = await RunAsync(ping);

        var pingAck = Assert.IsType<PingFrame>(Assert.Single(serverBound));
        Assert.True(pingAck.IsAck);
        Assert.True(pingAck.Data.Span.SequenceEqual(payload));
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionPing_should_not_trigger_response_when_ping_with_ack_received()
    {
        var pingAck = new PingFrame(new byte[8], isAck: true);

        var (_, serverBound) = await RunAsync(pingAck);

        Assert.Empty(serverBound);
    }

    [Fact(Timeout = 10_000)]
    public async Task Http2ConnectionPing_should_send_ping_response_on_stream_zero()
    {
        var ping = new PingFrame(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA, 0xF9, 0xF8 }, isAck: false);

        var (_, serverBound) = await RunAsync(ping);

        var pingAck = Assert.IsType<PingFrame>(Assert.Single(serverBound));
        Assert.Equal(0, pingAck.StreamId);
        Assert.True(pingAck.IsAck);
    }
}
