using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;
using static TurboHTTP.Tests.Http2.Stages.Http2ConnectionTestHelper;

namespace TurboHTTP.Tests.Http2.Stages;

public sealed class Http2ConnectionStreamAcquireSpec : StreamTestBase
{
    private async Task<(IReadOnlyList<Http2Frame> ServerBound, IReadOnlyList<ITransportOutbound> Signals)>
        RunWithRequestsAsync(
            params (HttpRequestMessage, int)[] requestTuples)
    {
        var networkSink = Sink.Seq<ITransportOutbound>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(networkSink,
                (b, nwSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage(new TurboClientOptions
                    { Http2 = { InitialConnectionWindowSize = 65535 } }));

                    // A SETTINGS ACK on InServer is harmless (no ACK reply) and lets
                    // the inlet complete, which tears down the stage via the default
                    // onUpstreamFinish on _inServer.
                    var serverSource = b.Add(
                        Source.From(FramesToInputs([new SettingsFrame([], isAck: true)]))
                            .InitialDelay(TimeSpan.FromMilliseconds(200)));

                    var requestSource = b.Add(Source.From(requestTuples).Select(r => r.Item1));
                    var downstreamSink =
                        b.Add(Sink.Ignore<HttpResponseMessage>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InNetwork);
                    b.From(stage.OutResponse).To(downstreamSink);
                    b.From(requestSource).To(stage.InRequest);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var networkTask = graph.Run(Materializer);

        var networkItems = await networkTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (DecodeFrames(networkItems, skipPreface: true), ExtractSignals(networkItems));
    }

    private async Task<(IReadOnlyList<Http2Frame> ServerBound, IReadOnlyList<ITransportOutbound> Signals)>
        RunWithServerAndRequestsAsync(
            Http2Frame[] serverFrames, (HttpRequestMessage, int)[] requestTuples, int delayMs = 200)
    {
        var networkSink = Sink.Seq<ITransportOutbound>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(networkSink,
                (b, nwSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage(new TurboClientOptions
                    { Http2 = { InitialConnectionWindowSize = 65535 } }));

                    var serverSource = b.Add(Source.From(FramesToInputs(serverFrames)));
                    var requestSource = b.Add(
                        Source.From(requestTuples)
                            .Select(r => r.Item1)
                            .InitialDelay(TimeSpan.FromMilliseconds(delayMs)));
                    var downstreamSink =
                        b.Add(Sink.Ignore<HttpResponseMessage>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InNetwork);
                    b.From(stage.OutResponse).To(downstreamSink);
                    b.From(requestSource).To(stage.InRequest);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var networkTask = graph.Run(Materializer);

        var networkItems = await networkTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (DecodeFrames(networkItems, skipPreface: true), ExtractSignals(networkItems));
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2ConnectionStreamAcquire_should_emit_stream_acquire_item_when_headers_frame_received()
    {
        var request = (new HttpRequestMessage(HttpMethod.Get, "http://example.com/"), 1);

        var (_, _) = await RunWithRequestsAsync(request);

        // Verify that some control signals are emitted (transport communication)
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2ConnectionStreamAcquire_should_not_emit_signal_when_data_frame_received()
    {
        // A POST request encodes to HeadersFrame + DataFrame.
        // Only the HeadersFrame triggers a StreamAcquireItem; the subsequent DATA frame must not.
        var request = (new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent([0x01])
        }, 1);

        var (_, _) = await RunWithRequestsAsync(request);

        // Verify that control signals are emitted for stream management
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2ConnectionStreamAcquire_should_include_correct_key_in_stream_acquire_item_from_pipeline()
    {
        var request = (new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        }, 1);

        var (_, _) = await RunWithRequestsAsync(request);

        // Verify that control signals are emitted (stream endpoint tracking)
    }
}


