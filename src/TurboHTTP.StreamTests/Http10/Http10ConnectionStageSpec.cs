using System.Net;
using System.Text;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http10;

/// <summary>
/// Tests the unified HTTP/1.0 connection stage that consolidates encoding, decoding,
/// and correlation into a single <see cref="Http10ConnectionStage"/>.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http10ConnectionStage"/>.
/// RFC 1945: HTTP/1.0 request/response cycle with single in-flight request.
/// </remarks>
public sealed class Http10ConnectionStageSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/")
    {
        return new HttpRequestMessage(HttpMethod.Get, $"http://example.com{path}")
        {
            Version = new Version(1, 0)
        };
    }

    private static NetworkBuffer MakeResponseBuffer(string raw)
    {
        var bytes = Encoding.ASCII.GetBytes(raw);
        var buf = NetworkBuffer.Rent(bytes.Length);
        bytes.CopyTo(buf.FullMemory.Span);
        buf.Length = bytes.Length;
        return buf;
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_encode_request_and_emit_on_network_outlet()
    {
        var stage = new Http10ConnectionStage();

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<IInputItem>();
        var networkSub = this.CreateManualSubscriberProbe<IOutputItem>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InApp);
            b.From(server).To(s.InServer);
            b.From(s.OutNetwork).To(netSink);
            b.From(s.OutResponse).To(resSink);

            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSubscription = await networkSub.ExpectSubscriptionAsync();
        var resSubscription = await responseSub.ExpectSubscriptionAsync();
        var appSubscription = await appProbe.ExpectSubscriptionAsync();
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync();

        // Pull on network outlet to signal demand
        netSubscription.Request(10);
        resSubscription.Request(10);

        // Send a request
        appSubscription.SendNext(MakeRequest("/test"));

        // Should get StreamAcquireItem + NetworkBuffer on network outlet
        var item1 = await networkSub.ExpectNextAsync();
        Assert.IsType<StreamAcquireItem>(item1);

        var item2 = await networkSub.ExpectNextAsync();
        var buffer = Assert.IsType<NetworkBuffer>(item2);
        var encoded = Encoding.ASCII.GetString(buffer.Span);
        Assert.StartsWith("GET /test HTTP/1.0\r\n", encoded);
        buffer.Dispose();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6")]
    public async Task Http10ConnectionStage_should_decode_response_and_correlate_with_request()
    {
        var stage = new Http10ConnectionStage();

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<IInputItem>();
        var networkSub = this.CreateManualSubscriberProbe<IOutputItem>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InApp);
            b.From(server).To(s.InServer);
            b.From(s.OutNetwork).To(netSink);
            b.From(s.OutResponse).To(resSink);

            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSubscription = await networkSub.ExpectSubscriptionAsync();
        var resSubscription = await responseSub.ExpectSubscriptionAsync();
        var appSubscription = await appProbe.ExpectSubscriptionAsync();
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync();

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Send request
        appSubscription.SendNext(MakeRequest("/hello"));

        // Consume outbound items (StreamAcquire + NetworkBuffer)
        await networkSub.ExpectNextAsync();
        await networkSub.ExpectNextAsync();

        // Send response from server
        var responseRaw = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello";
        serverSubscription.SendNext(MakeResponseBuffer(responseRaw));

        // Should get correlated response
        var response = await responseSub.ExpectNextAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.RequestMessage);
        Assert.Equal("/hello", response.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-7.2.2")]
    public async Task Http10ConnectionStage_should_emit_connection_reuse_close_for_http10()
    {
        var stage = new Http10ConnectionStage();

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<IInputItem>();
        var networkSub = this.CreateManualSubscriberProbe<IOutputItem>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InApp);
            b.From(server).To(s.InServer);
            b.From(s.OutNetwork).To(netSink);
            b.From(s.OutResponse).To(resSink);

            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSubscription = await networkSub.ExpectSubscriptionAsync();
        var resSubscription = await responseSub.ExpectSubscriptionAsync();
        var appSubscription = await appProbe.ExpectSubscriptionAsync();
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync();

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Send request + response
        appSubscription.SendNext(MakeRequest());

        // StreamAcquire + NetworkBuffer
        await networkSub.ExpectNextAsync();
        await networkSub.ExpectNextAsync();

        serverSubscription.SendNext(MakeResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nOK"));

        // Response
        await responseSub.ExpectNextAsync();

        // ConnectionReuseItem should follow on network outlet
        var reuseItem = await networkSub.ExpectNextAsync();
        var connectionReuse = Assert.IsType<ConnectionReuseItem>(reuseItem);
        // HTTP/1.0 default is close (RFC 1945)
        Assert.False(connectionReuse.Decision.CanReuse);
        connectionReuse.Return();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-7.2")]
    public async Task Http10ConnectionStage_should_emit_pipeline_retry_when_server_closes_with_inflight_request()
    {
        var stage = new Http10ConnectionStage();

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<IInputItem>();
        var networkSub = this.CreateManualSubscriberProbe<IOutputItem>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InApp);
            b.From(server).To(s.InServer);
            b.From(s.OutNetwork).To(netSink);
            b.From(s.OutResponse).To(resSink);

            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSubscription = await networkSub.ExpectSubscriptionAsync();
        var resSubscription = await responseSub.ExpectSubscriptionAsync();
        var appSubscription = await appProbe.ExpectSubscriptionAsync();
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync();

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Send a request
        var request = MakeRequest("/test");
        appSubscription.SendNext(request);

        // Consume StreamAcquire + NetworkBuffer
        await networkSub.ExpectNextAsync();
        await networkSub.ExpectNextAsync();

        // Server closes abruptly without sending a response
        serverSubscription.SendComplete();

        // Should emit PipelineRetryItem with the original request
        var retryItem = await networkSub.ExpectNextAsync();
        var pipelineRetry = Assert.IsType<PipelineRetryItem>(retryItem);
        Assert.Same(request, pipelineRetry.Request);
    }
}
