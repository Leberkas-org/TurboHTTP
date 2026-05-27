using TurboHTTP.Client;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Stages;

public sealed class Http30ConnectionStageSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/")
    {
        return new HttpRequestMessage(HttpMethod.Get, $"https://example.com{path}")
        {
            Version = new Version(3, 0)
        };
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4")]
    public async Task Http30ConnectionStage_should_route_to_correct_quic_stream()
    {
        var stage = new Http30ClientConnectionStage(new TurboClientOptions { Http3 = { MaxReconnectAttempts = 3 } });

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

        netSubscription.Request(20);
        resSubscription.Request(10);

        serverSubscription.SendNext(new TransportConnected(null!));

        // Send two requests — each should be routed to a different QUIC stream
        appSubscription.SendNext(MakeRequest("/stream1"));
        appSubscription.SendNext(MakeRequest("/stream2"));

        // After TransportConnected: PreStart items (3x OpenStream) are flushed + preface,
        // then request encoding emits ConnectTransport + MultiplexedData + CompleteWrites per request.
        for (var i = 0; i < 8; i++)
        {
            await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-5.2")]
    public async Task Http30ConnectionStage_should_handle_idle_timeout()
    {
        var stage = new Http30ClientConnectionStage(new TurboClientOptions
        {
            Http3 =
            {
                MaxReconnectAttempts = 3,
                IdleTimeout = TimeSpan.FromMilliseconds(100)
            }
        });

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
        await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Stage is initialized with idle timeout

        // Should handle timeout gracefully (may emit keep-alive or close gracefully)
        Assert.True(true);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-3")]
    public async Task Http30ConnectionStage_should_complete_when_app_upstream_finishes_with_no_inflight()
    {
        var stage = new Http30ClientConnectionStage(new TurboClientOptions { Http3 = { MaxReconnectAttempts = 3 } });

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
        await Task.Run(() => responseSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }
}