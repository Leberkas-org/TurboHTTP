using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.StreamTests.Http2;

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
        var stage = new Http20ConnectionStage(maxReconnectAttempts: 3);

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<IInputItem>();
        var networkSub = this.CreateManualSubscriberProbe<IOutputItem>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            b.From(Source.FromPublisher(appProbe)).To(s.InApp);
            b.From(Source.FromPublisher(serverProbe)).To(s.InServer);
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
        Assert.IsType<NetworkBuffer>(preface);

        // Send a request
        appSub.SendNext(MakeRequest());
        var acquire = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(acquire);
        var headers = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<NetworkBuffer>(headers);

        // Abrupt TCP close with no GOAWAY — in-flight request exists
        serverSub.SendNext(new CloseSignalItem(TlsCloseKind.AbruptClose));

        // Stage must emit ReconnectItem instead of failing
        var reconnect = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ReconnectItem>(reconnect);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9113-6.8")]
    public async Task Http20ConnectionStage_should_fail_when_max_reconnect_attempts_exceeded()
    {
        var stage = new Http20ConnectionStage(maxReconnectAttempts: 1);

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<IInputItem>();
        var networkSub = this.CreateManualSubscriberProbe<IOutputItem>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            b.From(Source.FromPublisher(appProbe)).To(s.InApp);
            b.From(Source.FromPublisher(serverProbe)).To(s.InServer);
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
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // StreamAcquireItem
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // HEADERS frame

        // First drop → reconnect attempt 1 (hits max immediately)
        serverSub.SendNext(new CloseSignalItem(TlsCloseKind.AbruptClose));
        var reconnect = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ReconnectItem>(reconnect);

        // Reconnect fails → CloseSignalItem again (attempt 2 exceeds max of 1)
        serverSub.SendNext(new CloseSignalItem(TlsCloseKind.AbruptClose));

        // Stage should fail — error propagates to response subscriber
        await Task.Run(() => responseSub.ExpectError(), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9113-6.8")]
    public async Task Http20ConnectionStage_should_complete_normally_on_close_with_no_inflight()
    {
        var stage = new Http20ConnectionStage(maxReconnectAttempts: 3);

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<IInputItem>();
        var networkSub = this.CreateManualSubscriberProbe<IOutputItem>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            b.From(Source.FromPublisher(appProbe)).To(s.InApp);
            b.From(Source.FromPublisher(serverProbe)).To(s.InServer);
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
        serverSub.SendNext(new CloseSignalItem(TlsCloseKind.CleanClose));

        await Task.Run(() => networkSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }
}
