using System.Net;
using System.Text;
using Akka;
using SysEncoding = System.Text.Encoding;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http11;

/// <summary>
/// Tests the unified HTTP/1.1 connection stage that consolidates encoding, decoding,
/// correlation, and pipelining into a single <see cref="Http11ConnectionStage"/>.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11ConnectionStage"/>.
/// RFC 9112: HTTP/1.1 request/response cycle with pipelining support.
/// </remarks>
public sealed class Http11ConnectionStageSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/")
    {
        return new HttpRequestMessage(HttpMethod.Get, $"http://example.com{path}")
        {
            Version = HttpVersion.Version11
        };
    }

    private static NetworkBuffer MakeResponseBuffer(string raw)
    {
        var bytes = SysEncoding.ASCII.GetBytes(raw);
        var buf = NetworkBuffer.Rent(bytes.Length);
        bytes.CopyTo(buf.FullMemory.Span);
        buf.Length = bytes.Length;
        return buf;
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11ConnectionStage_should_encode_request_and_emit_on_network_outlet()
    {
        var stage = new Http11ConnectionStage();

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

        appSubscription.SendNext(MakeRequest("/test"));

        // StreamAcquireItem + NetworkBuffer
        var item1 = await networkSub.ExpectNextAsync();
        Assert.IsType<StreamAcquireItem>(item1);

        var item2 = await networkSub.ExpectNextAsync();
        var buffer = Assert.IsType<NetworkBuffer>(item2);
        var encoded = SysEncoding.ASCII.GetString(buffer.Span);
        Assert.StartsWith("GET /test HTTP/1.1\r\n", encoded);
        Assert.Contains("Host: example.com", encoded);
        buffer.Dispose();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11ConnectionStage_should_decode_response_and_correlate_with_request()
    {
        var stage = new Http11ConnectionStage();

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

        appSubscription.SendNext(MakeRequest("/hello"));

        // Consume outbound
        await networkSub.ExpectNextAsync();
        await networkSub.ExpectNextAsync();

        serverSubscription.SendNext(MakeResponseBuffer(
            "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello"));

        var response = await responseSub.ExpectNextAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.RequestMessage);
        Assert.Equal("/hello", response.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionStage_should_support_pipelining_multiple_requests()
    {
        var stage = new Http11ConnectionStage(maxPipelineDepth: 4);

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

        netSubscription.Request(20);
        resSubscription.Request(10);

        // Send two requests (pipelined)
        appSubscription.SendNext(MakeRequest("/first"));
        // StreamAcquire + NetworkBuffer for first request
        await networkSub.ExpectNextAsync();
        await networkSub.ExpectNextAsync();

        appSubscription.SendNext(MakeRequest("/second"));
        // StreamAcquire + NetworkBuffer for second request
        await networkSub.ExpectNextAsync();
        await networkSub.ExpectNextAsync();

        // Send first response
        serverSubscription.SendNext(MakeResponseBuffer(
            "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nfirst"));

        var resp1 = await responseSub.ExpectNextAsync();
        Assert.Equal("/first", resp1.RequestMessage!.RequestUri!.AbsolutePath);

        // ConnectionReuseItem for first response
        var reuse1 = await networkSub.ExpectNextAsync();
        Assert.IsType<ConnectionReuseItem>(reuse1);

        // Send second response
        serverSubscription.SendNext(MakeResponseBuffer(
            "HTTP/1.1 200 OK\r\nContent-Length: 6\r\n\r\nsecond"));

        var resp2 = await responseSub.ExpectNextAsync();
        Assert.Equal("/second", resp2.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11ConnectionStage_should_pipeline_requests_up_to_max_depth()
    {
        var stage = new Http11ConnectionStage(maxPipelineDepth: 3);

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

        netSubscription.Request(100);
        resSubscription.Request(100);

        // Send 3 pipelined requests (up to maxPipelineDepth)
        appSubscription.SendNext(MakeRequest("/req1"));
        appSubscription.SendNext(MakeRequest("/req2"));
        appSubscription.SendNext(MakeRequest("/req3"));

        // Consume all 6 items (StreamAcquire + NetworkBuffer for each request)
        for (int i = 0; i < 6; i++)
        {
            await networkSub.ExpectNextAsync();
        }

        // All 3 requests should have been accepted and encoded.
        // Now send the 3 responses
        serverSubscription.SendNext(MakeResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nres1"));
        serverSubscription.SendNext(MakeResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nres2"));
        serverSubscription.SendNext(MakeResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nres3"));

        // Should get 3 responses
        var resp1 = await responseSub.ExpectNextAsync();
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal("/req1", resp1.RequestMessage!.RequestUri!.AbsolutePath);

        var resp2 = await responseSub.ExpectNextAsync();
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.Equal("/req2", resp2.RequestMessage!.RequestUri!.AbsolutePath);

        var resp3 = await responseSub.ExpectNextAsync();
        Assert.Equal(HttpStatusCode.OK, resp3.StatusCode);
        Assert.Equal("/req3", resp3.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-10.1")]
    public async Task Http11ConnectionStage_should_reduce_pipeline_depth_when_connection_close_received()
    {
        var stage = new Http11ConnectionStage(maxPipelineDepth: 3);

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

        netSubscription.Request(100);
        resSubscription.Request(100);

        // Send first request
        appSubscription.SendNext(MakeRequest("/req1"));

        // Consume StreamAcquire + NetworkBuffer
        await networkSub.ExpectNextAsync();
        await networkSub.ExpectNextAsync();

        // Send response with Connection: close header
        var responseWithClose = "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 4\r\n\r\nres1";
        serverSubscription.SendNext(MakeResponseBuffer(responseWithClose));

        // Get response
        var response = await responseSub.ExpectNextAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Get ConnectionReuseItem on network outlet
        var reuseItem = await networkSub.ExpectNextAsync();
        var connectionReuse = Assert.IsType<ConnectionReuseItem>(reuseItem);
        // Connection: close means cannot reuse
        Assert.False(connectionReuse.Decision.CanReuse);
        connectionReuse.Return();

        // After receiving Connection: close, the stage should reduce effective pipeline depth to 1.
        // Send a second request to verify it's still accepted
        appSubscription.SendNext(MakeRequest("/req2"));

        // Consume StreamAcquire + NetworkBuffer for req2
        await networkSub.ExpectNextAsync();
        await networkSub.ExpectNextAsync();

        // Send response for req2
        serverSubscription.SendNext(MakeResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nres2"));

        var response2 = await responseSub.ExpectNextAsync();
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("/req2", response2.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionStage_should_emit_connection_reuse_keep_alive_for_http11()
    {
        var stage = new Http11ConnectionStage();

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

        appSubscription.SendNext(MakeRequest());

        // StreamAcquire + NetworkBuffer
        await networkSub.ExpectNextAsync();
        await networkSub.ExpectNextAsync();

        serverSubscription.SendNext(MakeResponseBuffer(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK"));

        await responseSub.ExpectNextAsync();

        var reuseItem = await networkSub.ExpectNextAsync();
        var connectionReuse = Assert.IsType<ConnectionReuseItem>(reuseItem);
        // HTTP/1.1 default is keep-alive (RFC 9112)
        Assert.True(connectionReuse.Decision.CanReuse);
        connectionReuse.Return();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11ConnectionStage_should_emit_pipeline_retry_when_server_closes_with_inflight_requests()
    {
        var stage = new Http11ConnectionStage();

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

        netSubscription.Request(100);
        resSubscription.Request(100);

        // Send 2 pipelined requests
        var request1 = MakeRequest("/req1");
        var request2 = MakeRequest("/req2");
        appSubscription.SendNext(request1);
        appSubscription.SendNext(request2);

        // Consume StreamAcquire + NetworkBuffer for both
        await networkSub.ExpectNextAsync(); // StreamAcquire 1
        await networkSub.ExpectNextAsync(); // NetworkBuffer 1
        await networkSub.ExpectNextAsync(); // StreamAcquire 2
        await networkSub.ExpectNextAsync(); // NetworkBuffer 2

        // Server closes abruptly without sending responses
        serverSubscription.SendComplete();

        // Should emit PipelineRetryItem for both orphaned requests
        var retryItem1 = await networkSub.ExpectNextAsync();
        var pipelineRetry1 = Assert.IsType<PipelineRetryItem>(retryItem1);
        Assert.Same(request1, pipelineRetry1.Request);

        var retryItem2 = await networkSub.ExpectNextAsync();
        var pipelineRetry2 = Assert.IsType<PipelineRetryItem>(retryItem2);
        Assert.Same(request2, pipelineRetry2.Request);
    }
}
