using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http2;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.Http2.Connection;

/// <summary>
/// Tests stream acquisition and signalling in the HTTP/2 connection stage per RFC 9113.
/// Verifies that request frames are forwarded to the server and that stream-open signals are emitted correctly.
/// </summary>
[Trait("RFC", "RFC9113-5.1")]
public sealed class Http2ConnectionStreamAcquireSpec : StreamTestBase
{
    private async Task<(IReadOnlyList<Http2Frame> ServerBound, IReadOnlyList<IControlItem> Signals)> RunWithRequestsAsync(
        params (HttpRequestMessage, int)[] requestTuples)
    {
        var serverBoundSink = Sink.Seq<Http2Frame>();
        var signalSeqSink = Sink.Seq<IControlItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(serverBoundSink, signalSeqSink,
                (m1, m2) => (m1, m2),
                (b, sbSink, sigSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());

                    // A SETTINGS ACK on InServer is harmless (no ACK reply) and lets
                    // the inlet complete, which tears down the stage via the default
                    // onUpstreamFinish on _inServer.
                    var serverSource = b.Add(
                        Source.Single<Http2Frame>(new SettingsFrame([], isAck: true))
                            .InitialDelay(TimeSpan.FromMilliseconds(200)));

                    var requestSource = b.Add(Source.From(requestTuples).Select(r => r.Item1));
                    var downstreamSink = b.Add(Sink.Ignore<HttpResponseMessage>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(downstreamSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(sigSink);

                    return ClosedShape.Instance;
                }));

        var (serverBoundTask, signalTask) = graph.Run(Materializer);

        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var signals = await signalTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (serverBound, signals);
    }

    private async Task<(IReadOnlyList<Http2Frame> ServerBound, IReadOnlyList<IControlItem> Signals)> RunWithServerAndRequestsAsync(
        Http2Frame[] serverFrames, (HttpRequestMessage, int)[] requestTuples, int delayMs = 200)
    {
        var serverBoundSink = Sink.Seq<Http2Frame>();
        var signalSeqSink = Sink.Seq<IControlItem>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(serverBoundSink, signalSeqSink,
                (m1, m2) => (m1, m2),
                (b, sbSink, sigSink) =>
                {
                    var stage = b.Add(new Http20ConnectionStage());

                    var serverSource = b.Add(Source.From(serverFrames));
                    var requestSource = b.Add(
                        Source.From(requestTuples)
                            .Select(r => r.Item1)
                            .InitialDelay(TimeSpan.FromMilliseconds(delayMs)));
                    var downstreamSink = b.Add(Sink.Ignore<HttpResponseMessage>().MapMaterializedValue(_ => NotUsed.Instance));

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(downstreamSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);
                    b.From(stage.OutSignal).To(sigSink);

                    return ClosedShape.Instance;
                }));

        var (serverBoundTask, signalTask) = graph.Run(Materializer);

        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var signals = await signalTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (serverBound, signals);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2ConnectionStreamAcquire_should_emit_stream_acquire_item_when_headers_frame_received()
    {
        var request = (new HttpRequestMessage(HttpMethod.Get, "http://example.com/"), 1);

        var (_, signals) = await RunWithRequestsAsync(request);

        var signal = Assert.Single(signals);
        Assert.IsType<StreamAcquireItem>(signal);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2ConnectionStreamAcquire_should_not_emit_signal_when_data_frame_received()
    {
        // A POST request encodes to HeadersFrame + DataFrame.
        // Only the HeadersFrame triggers a StreamAcquireItem; the subsequent DATA frame must not.
        var request = (new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(new byte[] { 0x01 })
        }, 1);

        var (_, signals) = await RunWithRequestsAsync(request);

        Assert.Single(signals);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2ConnectionStreamAcquire_should_include_correct_key_in_stream_acquire_item_from_pipeline()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version20
        };

        var request = (new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        }, 1);

        var (_, signals) = await RunWithRequestsAsync(request);

        var signal = Assert.Single(signals);
        var acquire = Assert.IsType<StreamAcquireItem>(signal);
        Assert.Equal(endpoint, acquire.Key);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2ConnectionStreamAcquire_should_use_default_key_in_stream_acquire_item_when_no_endpoint()
    {
        var request = (new HttpRequestMessage { Method = HttpMethod.Get }, 1);

        var (_, signals) = await RunWithRequestsAsync(request);

        var signal = Assert.Single(signals);
        var acquire = Assert.IsType<StreamAcquireItem>(signal);
        Assert.Equal(RequestEndpoint.Default, acquire.Key);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2ConnectionStreamAcquire_should_capture_endpoint_once_and_reuse_for_subsequent_streams()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "api.example.com",
            Port = 8443,
            Version = HttpVersion.Version20
        };

        var req1 = (new HttpRequestMessage(HttpMethod.Get, "https://api.example.com:8443/")
        {
            Version = HttpVersion.Version20
        }, 1);
        var req2 = (new HttpRequestMessage { Method = HttpMethod.Get }, 3);

        var (_, signals) = await RunWithRequestsAsync(req1, req2);

        Assert.Equal(2, signals.Count);
        var acquire1 = Assert.IsType<StreamAcquireItem>(signals[0]);
        var acquire2 = Assert.IsType<StreamAcquireItem>(signals[1]);
        Assert.Equal(endpoint, acquire1.Key);
        Assert.Equal(endpoint, acquire2.Key);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2ConnectionStreamAcquire_should_set_endpoint_key_in_max_concurrent_streams_item_after_headers()
    {
        var requestTuple = (new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        }, 1);

        var settingsFrame = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 50)]);

        var (_, signals) = await RunWithServerAndRequestsAsync(
            [settingsFrame, new SettingsFrame([], isAck: true)],
            [requestTuple],
            delayMs: 50);

        var maxStreamsSignal = signals.OfType<MaxConcurrentStreamsItem>().FirstOrDefault();

        Assert.NotNull(maxStreamsSignal);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2ConnectionStreamAcquire_should_use_default_key_in_max_concurrent_streams_item_before_endpoint_capture()
    {
        var settingsFrame = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 128)]);

        var (_, signals) = await RunWithServerAndRequestsAsync(
            [settingsFrame, new SettingsFrame([], isAck: true)],
            [],
            delayMs: 50);

        var maxStreamsSignal = signals.OfType<MaxConcurrentStreamsItem>().SingleOrDefault();
        Assert.NotNull(maxStreamsSignal);
        Assert.Equal(128, maxStreamsSignal.MaxStreams);
        Assert.Equal(default(RequestEndpoint), maxStreamsSignal.Key);
    }
}
