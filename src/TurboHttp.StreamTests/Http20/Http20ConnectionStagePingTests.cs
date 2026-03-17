using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20ConnectionStagePingTests : StreamTestBase
{
    /// <summary>
    /// Runs the Http20ConnectionStage with the given server frames (arriving on ServerIn).
    /// Returns (downstream frames from AppOut, server-bound frames from ServerOut).
    /// </summary>
    private async Task<(IReadOnlyList<Http2Frame> Downstream, IReadOnlyList<Http2Frame> ServerBound)> RunAsync(
        params Http2Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<Http2Frame>();
        var serverBoundSink = Sink.Seq<Http2Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());
                    var serverSource = b.Add(Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<Http2Frame>());
                    var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.ServerIn);
                    b.From(stage.AppOut).To(dsSink);
                    b.From(requestSource).To(stage.AppIn);
                    b.From(stage.ServerOut).To(sbSink);
                    b.From(stage.OutletSignal).To(signalSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5));
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5));

        return (downstream, serverBound);
    }

    // ─── 20CP-001: PING without ACK → PING with ACK sent back ───────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.7-20CP-001: PING without ACK produces PING ACK response")]
    public async Task Ping_Without_Ack_Sends_Ack_Response()
    {
        var ping = new PingFrame(new byte[8], isAck: false);

        var (_, serverBound) = await RunAsync(ping);

        var response = Assert.Single(serverBound);
        var pingAck = Assert.IsType<PingFrame>(response);
        Assert.True(pingAck.IsAck, "Response must be a PING with ACK flag set");
    }

    // ─── 20CP-002: PING payload (8 bytes) → identical in ACK ─────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.7-20CP-002: PING ACK echoes identical 8-byte payload")]
    public async Task Ping_Ack_Echoes_Identical_Payload()
    {
        var payload = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF };
        var ping = new PingFrame(payload, isAck: false);

        var (_, serverBound) = await RunAsync(ping);

        var pingAck = Assert.IsType<PingFrame>(Assert.Single(serverBound));
        Assert.True(pingAck.IsAck);
        Assert.Equal(payload, pingAck.Data);
    }

    // ─── 20CP-003: PING with ACK flag → no new PING sent ────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.7-20CP-003: PING with ACK flag does not trigger another PING")]
    public async Task Ping_With_Ack_Does_Not_Trigger_Response()
    {
        var pingAck = new PingFrame(new byte[8], isAck: true);

        var (_, serverBound) = await RunAsync(pingAck);

        Assert.Empty(serverBound);
    }

    // ─── 20CP-004: PING on stream 0 → response on stream 0 ──────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.7-20CP-004: PING response is on stream 0")]
    public async Task Ping_Response_On_Stream_Zero()
    {
        var ping = new PingFrame([0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA, 0xF9, 0xF8], isAck: false);

        var (_, serverBound) = await RunAsync(ping);

        var pingAck = Assert.IsType<PingFrame>(Assert.Single(serverBound));
        Assert.Equal(0, pingAck.StreamId);
        Assert.True(pingAck.IsAck);
    }
}
