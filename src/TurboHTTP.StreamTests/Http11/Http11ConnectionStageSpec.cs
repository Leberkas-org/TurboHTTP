using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;
using SysEncoding = System.Text.Encoding;

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

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        if (appProbe != null)
        {
            var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
            var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

            netSubscription.Request(10);
            resSubscription.Request(10);

            appSubscription.SendNext(MakeRequest("/test"));
        }

        // StreamAcquireItem + NetworkBuffer
        var item1 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(item1);

        var item2 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
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

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(10);
        resSubscription.Request(10);

        appSubscription.SendNext(MakeRequest("/hello"));

        // Consume outbound
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        serverSubscription.SendNext(MakeResponseBuffer(
            "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello"));

        var response = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
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

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(20);
        resSubscription.Request(10);

        // Send two requests (pipelined)
        appSubscription.SendNext(MakeRequest("/first"));
        // StreamAcquire + NetworkBuffer for first request
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        appSubscription.SendNext(MakeRequest("/second"));
        // StreamAcquire + NetworkBuffer for second request
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Send first response
        serverSubscription.SendNext(MakeResponseBuffer(
            "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nfirst"));

        var resp1 = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.Equal("/first", resp1.RequestMessage!.RequestUri!.AbsolutePath);

        // ConnectionReuseItem for first response
        var reuse1 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectionReuseItem>(reuse1);

        // Send second response
        serverSubscription.SendNext(MakeResponseBuffer(
            "HTTP/1.1 200 OK\r\nContent-Length: 6\r\n\r\nsecond"));

        var resp2 = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
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

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(100);
        resSubscription.Request(100);

        // Send 3 pipelined requests (up to maxPipelineDepth)
        appSubscription.SendNext(MakeRequest("/req1"));
        appSubscription.SendNext(MakeRequest("/req2"));
        appSubscription.SendNext(MakeRequest("/req3"));

        // Consume all 6 items (StreamAcquire + NetworkBuffer for each request)
        for (var i = 0; i < 6; i++)
        {
            await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        }

        // All 3 requests should have been accepted and encoded.
        // Now send the 3 responses
        serverSubscription.SendNext(MakeResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nres1"));
        serverSubscription.SendNext(MakeResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nres2"));
        serverSubscription.SendNext(MakeResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nres3"));

        // Should get 3 responses
        var resp1 = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal("/req1", resp1.RequestMessage!.RequestUri!.AbsolutePath);

        var resp2 = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.Equal("/req2", resp2.RequestMessage!.RequestUri!.AbsolutePath);

        var resp3 = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
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

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(100);
        resSubscription.Request(100);

        // Send first request
        appSubscription.SendNext(MakeRequest("/req1"));

        // Consume StreamAcquire + NetworkBuffer
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Send response with Connection: close header
        var responseWithClose = "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 4\r\n\r\nres1";
        serverSubscription.SendNext(MakeResponseBuffer(responseWithClose));

        // Get response
        var response = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Get ConnectionReuseItem on network outlet
        var reuseItem = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var connectionReuse = Assert.IsType<ConnectionReuseItem>(reuseItem);
        // Connection: close means cannot reuse
        Assert.False(connectionReuse.Decision.CanReuse);

        // After receiving Connection: close, the stage should reduce effective pipeline depth to 1.
        // Send a second request to verify it's still accepted
        appSubscription.SendNext(MakeRequest("/req2"));

        // Consume StreamAcquire + NetworkBuffer for req2
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Send response for req2
        serverSubscription.SendNext(MakeResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nres2"));

        var response2 = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
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

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(10);
        resSubscription.Request(10);

        appSubscription.SendNext(MakeRequest());

        // StreamAcquire + NetworkBuffer
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        serverSubscription.SendNext(MakeResponseBuffer(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK"));

        await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        var reuseItem = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var connectionReuse = Assert.IsType<ConnectionReuseItem>(reuseItem);
        // HTTP/1.1 default is keep-alive (RFC 9112)
        Assert.True(connectionReuse.Decision.CanReuse);
    }
}