using System.Net;
using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Http10;

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

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        // Pull on network outlet to signal demand
        netSubscription.Request(10);
        resSubscription.Request(10);

        // Send a request
        appSubscription.SendNext(MakeRequest("/test"));

        // Should get StreamAcquireItem + NetworkBuffer on network outlet
        var item1 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(item1);

        var item2 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
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

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Send request
        appSubscription.SendNext(MakeRequest("/hello"));

        // Consume outbound items (StreamAcquire + NetworkBuffer)
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Send response from server
        var responseRaw = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello";
        serverSubscription.SendNext(MakeResponseBuffer(responseRaw));

        // Should get correlated response
        var response = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
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

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Send request + response
        appSubscription.SendNext(MakeRequest());

        // StreamAcquire + NetworkBuffer
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        serverSubscription.SendNext(MakeResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nOK"));

        // Response
        await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // ConnectionReuseItem should follow on network outlet
        var reuseItem = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var connectionReuse = Assert.IsType<ConnectionReuseItem>(reuseItem);
        // HTTP/1.0 default is close (RFC 1945)
        Assert.False(connectionReuse.Decision.CanReuse);
    }


    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_complete_stage_when_app_upstream_finishes_without_inflight()
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

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Complete app upstream without sending any request
        appSubscription.SendComplete();

        // Stage should complete
        await Task.Run(() => responseSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_complete_when_server_closes_and_no_response_pending()
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

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Server closes without any request/response activity
        serverSubscription.SendComplete();

        // Stage should complete
        await Task.Run(() => responseSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }
}
