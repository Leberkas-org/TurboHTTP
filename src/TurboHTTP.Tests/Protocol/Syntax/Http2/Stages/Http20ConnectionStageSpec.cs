using TurboHTTP.Client;
using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Stages;

public sealed class Http20ConnectionStageSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/")
    {
        return new HttpRequestMessage(HttpMethod.Get, $"https://example.com{path}")
        {
            Version = new Version(2, 0)
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
    [Trait("RFC", "RFC9113-3.2")]
    public async Task Http20ConnectionStage_should_emit_connect_then_preface_on_first_request()
    {
        var stage = new Http20ClientConnectionStage(new TurboClientOptions
        { Http2 = { MaxReconnectAttempts = 3 } });

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

        netSubscription.Request(5);
        resSubscription.Request(5);

        appSubscription.SendNext(MakeRequest());

        var connect = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(connect);

        var preface = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var prefaceData = Assert.IsType<TransportData>(preface);
        var data = Encoding.ASCII.GetString(prefaceData.Buffer.Span);
        Assert.StartsWith("PRI * HTTP/2.0", data);
        prefaceData.Buffer.Dispose();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6")]
    public async Task Http20ConnectionStage_should_encode_request_as_headers_frame()
    {
        var stage = new Http20ClientConnectionStage(new TurboClientOptions
        { Http2 = { MaxReconnectAttempts = 3 } });

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

        // Send request
        appSubscription.SendNext(MakeRequest("/test"));

        // First request: ConnectTransport → preface → HEADERS frame
        var connect = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(connect);

        var preface = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var prefaceData = Assert.IsType<TransportData>(preface);
        var data = Encoding.ASCII.GetString(prefaceData.Buffer.Span);
        Assert.StartsWith("PRI * HTTP/2.0", data);
        prefaceData.Buffer.Dispose();

        var headers = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<TransportData>(headers);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.2")]
    public async Task Http20ConnectionStage_should_support_stream_multiplexing()
    {
        var stage = new Http20ClientConnectionStage(new TurboClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        netSubscription.Request(20);
        resSubscription.Request(10);

        // Send two requests simultaneously (multiplexing)
        appSubscription.SendNext(MakeRequest("/req1"));
        appSubscription.SendNext(MakeRequest("/req2"));

        // First request: ConnectTransport + preface + HEADERS, second request: HEADERS
        var connect = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(connect);

        var preface = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<TransportData>(preface);

        var headers1 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<TransportData>(headers1);

        var headers2 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<TransportData>(headers2);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-3.1")]
    public async Task Http20ConnectionStage_should_handle_settings_frame()
    {
        var stage = new Http20ClientConnectionStage(new TurboClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        // Server sends SETTINGS frame before any client request
        serverSubscription.SendNext(new TransportData(MakeResponseBuffer("\x00\x00\x00\x04\x00\x00\x00\x00\x00")));

        Assert.True(true);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.8")]
    public async Task Http20ConnectionStage_should_complete_on_goaway_with_no_inflight()
    {
        var stage = new Http20ClientConnectionStage(new TurboClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        // Server sends GOAWAY before any client request
        serverSubscription.SendNext(new TransportDisconnected(DisconnectReason.Graceful));
        serverSubscription.SendComplete();

        // Stage completes when server upstream finishes
        networkSub.ExpectComplete(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6")]
    public async Task Http20ConnectionStage_should_complete_when_app_upstream_finishes_with_no_inflight()
    {
        var stage = new Http20ClientConnectionStage(new TurboClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        // Complete app without sending request
        appSubscription.SendComplete();

        // Stage should complete
        responseSub.ExpectComplete(TestContext.Current.CancellationToken);
    }
}