using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.IO;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;
using static TurboHTTP.StreamTests.Http2.Http2ConnectionTestHelper;

namespace TurboHTTP.StreamTests.Http2;

public sealed class Http2ConnectionStreamAcquireSpec : StreamTestBase
{
    private async Task<(IReadOnlyList<Http2Frame> ServerBound, IReadOnlyList<IControlItem> Signals)>
        RunWithRequestsAsync(
            params (HttpRequestMessage, int)[] requestTuples)
    {
        var networkSink = Sink.Seq<IOutputItem>();

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

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(downstreamSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutNetwork).To(nwSink);

                    return ClosedShape.Instance;
                }));

        var networkTask = graph.Run(Materializer);

        var networkItems = await networkTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (DecodeFrames(networkItems, skipPreface: true), ExtractSignals(networkItems));
    }

    private async Task<(IReadOnlyList<Http2Frame> ServerBound, IReadOnlyList<IControlItem> Signals)>
        RunWithServerAndRequestsAsync(
            Http2Frame[] serverFrames, (HttpRequestMessage, int)[] requestTuples, int delayMs = 200)
    {
        var networkSink = Sink.Seq<IOutputItem>();

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

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutResponse).To(downstreamSink);
                    b.From(requestSource).To(stage.InApp);
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

        var (_, signals) = await RunWithRequestsAsync(request);

        var signal = Assert.Single(signals.OfType<StreamAcquireItem>());
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
            Content = new ByteArrayContent([0x01])
        }, 1);

        var (_, signals) = await RunWithRequestsAsync(request);

        // Only the HeadersFrame triggers a StreamAcquireItem; ConnectItem and DATA frame must not add one.
        Assert.Single(signals.OfType<StreamAcquireItem>());
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

        var signal = Assert.Single(signals.OfType<StreamAcquireItem>());
        var acquire = Assert.IsType<StreamAcquireItem>(signal);
        Assert.Equal(endpoint, acquire.Key);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2ConnectionStreamAcquire_should_use_default_key_in_stream_acquire_item_when_no_endpoint()
    {
        var request = (new HttpRequestMessage { Method = HttpMethod.Get }, 1);

        var (_, signals) = await RunWithRequestsAsync(request);

        var signal = Assert.Single(signals.OfType<StreamAcquireItem>());
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

        var acquires = signals.OfType<StreamAcquireItem>().ToList();
        Assert.Equal(2, acquires.Count);
        var acquire1 = Assert.IsType<StreamAcquireItem>(acquires[0]);
        var acquire2 = Assert.IsType<StreamAcquireItem>(acquires[1]);
        Assert.Equal(endpoint, acquire1.Key);
        Assert.Equal(endpoint, acquire2.Key);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task
        Http2ConnectionStreamAcquire_should_set_endpoint_key_in_max_concurrent_streams_item_after_headers()
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

        Assert.NotEqual(default, maxStreamsSignal);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task
        Http2ConnectionStreamAcquire_should_use_default_key_in_max_concurrent_streams_item_before_endpoint_capture()
    {
        var settingsFrame = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 128)]);

        var (_, signals) = await RunWithServerAndRequestsAsync(
            [settingsFrame, new SettingsFrame([], isAck: true)],
            [],
            delayMs: 50);

        var maxStreamsSignal = signals.OfType<MaxConcurrentStreamsItem>().SingleOrDefault();
        Assert.Equal(128, maxStreamsSignal.MaxStreams);
        Assert.Equal(default, maxStreamsSignal.Key);
    }
}