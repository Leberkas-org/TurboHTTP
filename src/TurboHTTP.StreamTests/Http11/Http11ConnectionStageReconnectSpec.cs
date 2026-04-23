using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Servus.Akka.IO;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Http11;

public sealed class Http11ConnectionStageReconnectSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/") =>
        new(HttpMethod.Get, new Uri($"http://example.com{path}"))
        {
            Version = new Version(1, 1)
        };

    private static NetworkBuffer MakeResponseBuffer(string raw)
    {
        var bytes = Encoding.ASCII.GetBytes(raw);
        var buf = NetworkBuffer.Rent(bytes.Length);
        bytes.CopyTo(buf.FullMemory.Span);
        buf.Length = bytes.Length;
        return buf;
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionStage_should_reconnect_and_replay_request_on_connection_drop()
    {
        var stage = new Http11ConnectionStage(new TurboClientOptions
        { Http1 = { MaxPipelineDepth = 1, MaxReconnectAttempts = 1 } });

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

        // Send a request
        appSub.SendNext(MakeRequest());

        // Consume ConnectItem + StreamAcquireItem + NetworkBuffer
        var item0 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectItem>(item0);
        var item1 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(item1);
        var item2 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<NetworkBuffer>(item2);

        // Connection drops while request is in-flight
        serverSub.SendNext(new CloseSignalItem(TlsCloseKind.AbruptClose));

        // Stage must emit ConnectItem with IsReconnect
        var reconnectRaw = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var reconnect = Assert.IsType<ConnectItem>(reconnectRaw);
        Assert.True(reconnect.IsReconnect);

        // Simulate reconnect success → sends ConnectedSignalItem
        serverSub.SendNext(new ConnectedSignalItem { Key = reconnect.Key });

        // Stage must replay the request — expect StreamAcquireItem + NetworkBuffer again
        var item3 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<StreamAcquireItem>(item3);
        var item4 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<NetworkBuffer>(item4);

        // Now respond normally
        serverSub.SendNext(MakeResponseBuffer("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello"));

        var response = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionStage_should_complete_stage_when_max_reconnect_attempts_exceeded()
    {
        var stage = new Http11ConnectionStage(new TurboClientOptions
        { Http1 = { MaxPipelineDepth = 1, MaxReconnectAttempts = 1 } });

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

        appSub.SendNext(MakeRequest());
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // ConnectItem
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // StreamAcquireItem
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // NetworkBuffer

        // First drop → reconnect attempt 1 (hits max immediately)
        serverSub.SendNext(new CloseSignalItem(TlsCloseKind.AbruptClose));
        var reconnectRaw = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var reconnectItem2 = Assert.IsType<ConnectItem>(reconnectRaw);
        Assert.True(reconnectItem2.IsReconnect);

        // Reconnect fails → CloseSignalItem again (attempt 2 exceeds max of 1)
        serverSub.SendNext(new CloseSignalItem(TlsCloseKind.AbruptClose));

        // Stage should complete
        await Task.Run(() => responseSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionStage_should_not_reconnect_when_no_inflight_request_on_close()
    {
        var stage = new Http11ConnectionStage(new TurboClientOptions
        { Http1 = { MaxPipelineDepth = 1, MaxReconnectAttempts = 1 } });

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

        // No requests sent — connection just closes cleanly
        serverSub.SendNext(new CloseSignalItem(TlsCloseKind.CleanClose));

        // Stage completes immediately (no in-flight requests → no reconnect, just CompleteStage)
        await Task.Run(() => networkSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }
}