using TurboHTTP.Client;
using System.Net;
using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Stages;

public sealed class Http10ConnectionStageSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/")
    {
        return new HttpRequestMessage(HttpMethod.Get, $"http://example.com{path}")
        {
            Version = new Version(1, 0)
        };
    }

    private static TransportBuffer MakeResponseBuffer(string raw)
    {
        var bytes = Encoding.ASCII.GetBytes(raw);
        var buf = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buf.FullMemory.Span);
        buf.Length = bytes.Length;
        return buf;
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_encode_request_and_emit_on_network_outlet()
    {
        var stage = new Http10ConnectionStage(new TurboClientOptions());

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InRequest);
            b.From(server).To(s.InNetwork);
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

        // ConnectItem emitted first when endpoint is known from the first request
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Should get TransportBuffer on network outlet
        var item = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var buffer = Assert.IsType<TransportData>(item);
        var encoded = Encoding.ASCII.GetString(buffer.Buffer.Span);
        Assert.StartsWith("GET /test HTTP/1.0\r\n", encoded);
        buffer.Buffer.Dispose();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6")]
    public async Task Http10ConnectionStage_should_decode_response_and_correlate_with_request()
    {
        var stage = new Http10ConnectionStage(new TurboClientOptions());

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InRequest);
            b.From(server).To(s.InNetwork);
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

        // Consume outbound items (ConnectTransport + TransportData)
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Send response from server
        const string responseRaw = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello";
        serverSubscription.SendNext(new TransportData(MakeResponseBuffer(responseRaw)));

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
        var stage = new Http10ConnectionStage(new TurboClientOptions());

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InRequest);
            b.From(server).To(s.InNetwork);
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

        // ConnectTransport + TransportBuffer
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        serverSubscription.SendNext(
            new TransportData(MakeResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nOK")));

        // Response
        await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
    }


    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_complete_stage_when_app_upstream_finishes_without_inflight()
    {
        var stage = new Http10ConnectionStage(new TurboClientOptions());

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InRequest);
            b.From(server).To(s.InNetwork);
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
        var stage = new Http10ConnectionStage(new TurboClientOptions());

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InRequest);
            b.From(server).To(s.InNetwork);
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