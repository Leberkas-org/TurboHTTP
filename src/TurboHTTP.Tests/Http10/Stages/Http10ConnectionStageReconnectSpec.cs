using System.Net;
using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http10.Stages;

public sealed class Http10ConnectionStageReconnectSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest() =>
        new(HttpMethod.Get, new Uri("http://example.com/"))
        {
            Version = new Version(1, 0)
        };

    private static TransportBuffer MakeResponseBuffer(string raw)
    {
        var bytes = Encoding.ASCII.GetBytes(raw);
        var buf = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buf.FullMemory.Span);
        buf.Length = bytes.Length;
        return buf;
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_reconnect_and_replay_request_on_connection_drop()
    {
        var stage = new Http10ConnectionStage(new TurboClientOptions { Http1 = { MaxReconnectAttempts = 3 } });

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

        // Send a request
        appSub.SendNext(MakeRequest());

        // Consume ConnectTransport + TransportData
        var item0 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(item0);
        var item1 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var td = Assert.IsType<TransportData>(item1);
        td.Buffer.Dispose();

        // Connection drops while request is in-flight
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));

        // Stage must emit ConnectTransport (not fail or complete)
        var reconnectRaw = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var reconnect = Assert.IsType<ConnectTransport>(reconnectRaw);

        // Simulate TcpConnectionStage reconnect success → sends TransportConnected
        var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, reconnect.Options.Port);
        var localEndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        serverSub.SendNext(new TransportConnected(new ConnectionInfo(localEndPoint, remoteEndPoint, TransportProtocol.Tcp)));

        // Stage must replay the request — expect TransportData again
        var item2Retry = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var tdRetry = Assert.IsType<TransportData>(item2Retry);
        tdRetry.Buffer.Dispose();

        // Now respond normally
        var responseBuffer = MakeResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        serverSub.SendNext(new TransportData(responseBuffer));

        var response = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_complete_stage_when_max_reconnect_attempts_exceeded()
    {
        var stage = new Http10ConnectionStage(new TurboClientOptions { Http1 = { MaxReconnectAttempts = 1 } });

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

        appSub.SendNext(MakeRequest());
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // ConnectTransport
        var item = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // TransportData
        Assert.IsType<TransportData>(item);

        // First drop → reconnect attempt 1 (hits max immediately)
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));
        var reconnectRaw = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(reconnectRaw);

        // Reconnect fails → TransportDisconnected again (attempt 2 exceeds max of 1)
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));

        // Transport source completes after final disconnect
        serverSub.SendComplete();

        // Stage should complete
        await Task.Run(() => responseSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_not_reconnect_when_no_inflight_request_on_close()
    {
        var stage = new Http10ConnectionStage(new TurboClientOptions { Http1 = { MaxReconnectAttempts = 1 } });

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

        // No requests sent — connection just closes
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Graceful));
        serverSub.SendComplete();

        // Stage completes when server upstream finishes
        await Task.Run(() => networkSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }
}

