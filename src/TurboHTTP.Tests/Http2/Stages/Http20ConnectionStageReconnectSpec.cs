using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http2.Stages;

public sealed class Http20ConnectionStageReconnectSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/") =>
        new(HttpMethod.Get, $"https://example.com{path}")
        {
            Version = new Version(2, 0)
        };

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9113-6.8")]
    public async Task Http20ConnectionStage_should_emit_reconnect_item_on_abrupt_close_with_inflight()
    {
        var stage = new Http20ConnectionStage(new TurboClientOptions { Http2 = { MaxReconnectAttempts = 1 } });

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            b.From(Source.FromPublisher(appProbe)).To(s.InRequest);
            b.From(Source.FromPublisher(serverProbe)).To(s.InNetwork);
            b.From(s.OutNetwork).To(Sink.FromSubscriber(networkSub));
            b.From(s.OutResponse).To(Sink.FromSubscriber(responseSub));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSub = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSub = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSub = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSub.Request(20);
        resSub.Request(10);

        // Consume connection preface (emitted on first network pull)
        var preface = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<TransportData>(preface);

        // Send a request — first request also emits ConnectTransport before HEADERS
        appSub.SendNext(MakeRequest());
        var connectItem = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(connectItem);
        var headers = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var td = Assert.IsType<TransportData>(headers);
        td.Buffer.Dispose();

        // Abrupt TCP close with no GOAWAY — in-flight request exists
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));

        // Stage must emit ConnectTransport instead of failing (2nd ConnectTransport = reconnect)
        var reconnect = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(reconnect);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9113-6.8")]
    public async Task Http20ConnectionStage_should_fail_when_max_reconnect_attempts_exceeded()
    {
        var stage = new Http20ConnectionStage(new TurboClientOptions { Http2 = { MaxReconnectAttempts = 1 } });

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            b.From(Source.FromPublisher(appProbe)).To(s.InRequest);
            b.From(Source.FromPublisher(serverProbe)).To(s.InNetwork);
            b.From(s.OutNetwork).To(Sink.FromSubscriber(networkSub));
            b.From(s.OutResponse).To(Sink.FromSubscriber(responseSub));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSub = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSub = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSub = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSub.Request(20);
        resSub.Request(10);

        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // preface

        appSub.SendNext(MakeRequest());
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // ConnectTransport
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // HEADERS frame

        // First drop → reconnect attempt 1 (hits max immediately)
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));
        var reconnect = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(reconnect);

        // Reconnect fails → TransportDisconnected again (attempt 2 exceeds max of 1)
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));
        serverSub.SendComplete();

        // Stage completes when server upstream finishes
        await Task.Run(() => responseSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9113-6.8")]
    public async Task Http20ConnectionStage_should_complete_normally_on_close_with_no_inflight()
    {
        var stage = new Http20ConnectionStage(new TurboClientOptions { Http2 = { MaxReconnectAttempts = 1 } });

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            b.From(Source.FromPublisher(appProbe)).To(s.InRequest);
            b.From(Source.FromPublisher(serverProbe)).To(s.InNetwork);
            b.From(s.OutNetwork).To(Sink.FromSubscriber(networkSub));
            b.From(s.OutResponse).To(Sink.FromSubscriber(responseSub));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSub = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSub = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSub.Request(20);
        resSub.Request(10);

        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // preface

        // Close with no in-flight requests
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Graceful));
        serverSub.SendComplete();

        await Task.Run(() => networkSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }
}

