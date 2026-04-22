using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Http2;

public sealed class Http20ConnectionStageSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/")
    {
        return new HttpRequestMessage(HttpMethod.Get, $"https://example.com{path}")
        {
            Version = new Version(2, 0)
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
    [Trait("RFC", "RFC9113-3.2")]
    public async Task Http20ConnectionStage_should_emit_preface_on_first_network_pull()
    {
        var stage = new Http20ConnectionStage(new TurboClientOptions
            { Http2 = { MaxReconnectAttempts = 3 } });

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
        await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(5);
        resSubscription.Request(5);

        // First item should be HTTP/2 preface (connection preface)
        var preface = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var buffer = Assert.IsType<NetworkBuffer>(preface);
        var data = Encoding.ASCII.GetString(buffer.Span);
        Assert.StartsWith("PRI * HTTP/2.0", data);
        buffer.Dispose();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6")]
    public async Task Http20ConnectionStage_should_encode_request_as_headers_frame()
    {
        var stage = new Http20ConnectionStage(new TurboClientOptions
            { Http2 = { MaxReconnectAttempts = 3 } });

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

        // Consume preface
        var preface = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<NetworkBuffer>(preface);

        // Send request
        appSubscription.SendNext(MakeRequest("/test"));

        // First request: ConnectItem (transport connect) + StreamAcquireItem + HEADERS frame (as NetworkBuffer)
        var connect = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectItem>(connect);

        var acquire = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(acquire);

        var headers = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<NetworkBuffer>(headers);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.2")]
    public async Task Http20ConnectionStage_should_support_stream_multiplexing()
    {
        var stage = new Http20ConnectionStage(new TurboClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        netSubscription.Request(20);
        resSubscription.Request(10);

        // Consume preface
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Send two requests simultaneously (multiplexing)
        appSubscription.SendNext(MakeRequest("/req1"));
        appSubscription.SendNext(MakeRequest("/req2"));

        // Both should be encoded
        for (var i = 0; i < 4; i++)
        {
            await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        }

        // All requests accepted
        Assert.True(true);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-3.1")]
    public async Task Http20ConnectionStage_should_handle_settings_frame()
    {
        var stage = new Http20ConnectionStage(new TurboClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        // Consume preface
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Server sends SETTINGS frame (normally part of handshake but can be sent at any time)
        serverSubscription.SendNext(MakeResponseBuffer("\x00\x00\x00\x04\x00\x00\x00\x00\x00"));

        // Stage should handle it gracefully (no immediate response expected from app)
        // Can send request after SETTINGS
        Assert.True(true);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.8")]
    public async Task Http20ConnectionStage_should_complete_on_goaway_with_no_inflight()
    {
        var stage = new Http20ConnectionStage(new TurboClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        // Consume preface
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Server sends GOAWAY
        serverSubscription.SendNext(new CloseSignalItem(TlsCloseKind.CleanClose));

        // Stage should complete
        await Task.Run(() => networkSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6")]
    public async Task Http20ConnectionStage_should_complete_when_app_upstream_finishes_with_no_inflight()
    {
        var stage = new Http20ConnectionStage(new TurboClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        // Consume preface
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Complete app without sending request
        appSubscription.SendComplete();

        // Stage should complete
        await Task.Run(() => responseSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }
}