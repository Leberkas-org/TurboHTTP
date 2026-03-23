using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9112;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests the reconnection state machine of <see cref="ExtractOptionsStage"/>.
/// Verifies that <see cref="ConnectItem"/> signals are emitted correctly based on
/// <see cref="ConnectionReuseItem"/> feedback for HTTP/1.0, HTTP/1.1, and HTTP/2 scenarios.
/// </summary>
/// <remarks>
/// The stage is protocol-agnostic: it does not inspect <c>HttpVersion</c> when deciding
/// whether to reconnect. Protocol-specific behaviour is enforced upstream by
/// <see cref="ConnectionReuseEvaluator"/>, which always returns
/// <c>CanReuse = true</c> for HTTP/2 (multiplexing) and HTTP/1.1 (keep-alive default).
/// </remarks>
public sealed class ExtractOptionsStageReconnectTests : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(
        string url = "http://example.com/",
        Version? version = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (version is not null)
        {
            req.Version = version;
        }

        return req;
    }

    private static ConnectionReuseItem MakeReuseItem(bool canReuse)
    {
        var decision = canReuse
            ? ConnectionReuseDecision.KeepAlive("test-reuse")
            : ConnectionReuseDecision.Close("test-close");
        return new ConnectionReuseItem(RequestEndpoint.Default, decision);
    }

    /// <summary>
    /// Wires <see cref="ExtractOptionsStage"/> with manual publisher probes on both inlets
    /// and manual subscriber probes on both outlets, giving full demand/push control.
    /// </summary>
    private StageHarness SetupHarness()
    {
        var requestPub = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var reusePub = this.CreateManualPublisherProbe<IControlItem>();
        var signalProbe = this.CreateManualSubscriberProbe<IOutputItem>();
        var requestProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var stage = b.Add(new ExtractOptionsStage());
            var reqSrc = b.Add(Source.FromPublisher(requestPub));
            var reuseSrc = b.Add(Source.FromPublisher(reusePub));

            b.From(reqSrc).To(stage.In);
            b.From(reuseSrc).To(stage.InReuse);
            b.From(stage.OutSignal).To(Sink.FromSubscriber(signalProbe));
            b.From(stage.OutRequest).To(Sink.FromSubscriber(requestProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var reqPubSub = requestPub.ExpectSubscription();
        var reusePubSub = reusePub.ExpectSubscription();
        var signalSub = signalProbe.ExpectSubscription();
        var requestSub = requestProbe.ExpectSubscription();

        return new StageHarness(
            SendRequest: reqPubSub.SendNext,
            SendReuse: reusePubSub.SendNext,
            RequestSignalDemand: signalSub.Request,
            RequestOutputDemand: requestSub.Request,
            SignalProbe: signalProbe,
            RequestProbe: requestProbe);
    }

    /// <summary>
    /// Pushes the first request through the stage and collects the ConnectItem + request output.
    /// </summary>
    private static async Task<(ConnectItem Signal, HttpRequestMessage Request)> PushFirstRequestAsync(
        StageHarness h, HttpRequestMessage request, CancellationToken ct)
    {
        // Give demand to signal outlet → triggers Pull(In) since _connectItemSent is false
        h.RequestSignalDemand(1);
        // Give demand to request outlet
        h.RequestOutputDemand(1);
        // Push the request
        h.SendRequest(request);

        var signal = await h.SignalProbe.ExpectNextAsync(ct);
        var msg = await h.RequestProbe.ExpectNextAsync(ct);

        var connectItem = Assert.IsType<ConnectItem>(signal);
        return (connectItem, msg);
    }

    /// <summary>
    /// Pushes a subsequent request expecting a new ConnectItem (reconnect scenario).
    /// </summary>
    private static async Task<(ConnectItem Signal, HttpRequestMessage Request)> PushReconnectRequestAsync(
        StageHarness h, HttpRequestMessage request, CancellationToken ct)
    {
        h.RequestSignalDemand(1);
        h.RequestOutputDemand(1);
        h.SendRequest(request);

        var signal = await h.SignalProbe.ExpectNextAsync(ct);
        var msg = await h.RequestProbe.ExpectNextAsync(ct);

        var connectItem = Assert.IsType<ConnectItem>(signal);
        return (connectItem, msg);
    }

    /// <summary>
    /// Pushes a subsequent request expecting NO ConnectItem (reuse / no reconnect).
    /// </summary>
    private static async Task<HttpRequestMessage> PushPassthroughRequestAsync(
        StageHarness h, HttpRequestMessage request, CancellationToken ct)
    {
        h.RequestOutputDemand(1);
        h.SendRequest(request);

        return await h.RequestProbe.ExpectNextAsync(ct);
    }

    // ──── HTTP/1.0 Tests ────

    [Fact(Timeout = 5000, DisplayName = "EXT-RC-001: HTTP/1.0 first request emits ConnectItem")]
    public async Task HTTP10_FirstRequest_EmitsConnectItem()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var h = SetupHarness();
        var req = MakeRequest("http://example.com/h10", HttpVersion.Version10);

        var (signal, msg) = await PushFirstRequestAsync(h, req, cts.Token);

        Assert.Equal("example.com", signal.Options.Host);
        Assert.Equal(80, signal.Options.Port);
        Assert.Same(req, msg);
    }

    [Fact(Timeout = 5000, DisplayName = "EXT-RC-002: HTTP/1.0 second request after Close emits new ConnectItem")]
    public async Task HTTP10_SecondRequest_AfterReuseFalse_EmitsNewConnectItem()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var h = SetupHarness();

        // First request cycle
        var req1 = MakeRequest("http://example.com/1", HttpVersion.Version10);
        await PushFirstRequestAsync(h, req1, cts.Token);

        // Reuse feedback: connection closed (HTTP/1.0 default)
        h.SendReuse(MakeReuseItem(canReuse: false));

        // Second request → must emit new ConnectItem
        var req2 = MakeRequest("http://example.com/2", HttpVersion.Version10);
        var (signal2, msg2) = await PushReconnectRequestAsync(h, req2, cts.Token);

        Assert.IsType<ConnectItem>(signal2);
        Assert.Same(req2, msg2);
    }

    [Fact(Timeout = 5000, DisplayName = "EXT-RC-003: HTTP/1.0 third request after Reuse skips ConnectItem")]
    public async Task HTTP10_ThirdRequest_AfterReuseTrue_SkipsConnectItem()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var h = SetupHarness();

        // First request
        var req1 = MakeRequest("http://example.com/1", HttpVersion.Version10);
        await PushFirstRequestAsync(h, req1, cts.Token);

        // Reuse feedback: Close → triggers reconnect for second request
        h.SendReuse(MakeReuseItem(canReuse: false));

        // Second request → reconnect
        var req2 = MakeRequest("http://example.com/2", HttpVersion.Version10);
        await PushReconnectRequestAsync(h, req2, cts.Token);

        // Reuse feedback: Reuse (e.g., HTTP/1.0 server sent Connection: Keep-Alive)
        h.SendReuse(MakeReuseItem(canReuse: true));

        // Third request → no reconnect (reuse decision was KeepAlive)
        var req3 = MakeRequest("http://example.com/3", HttpVersion.Version10);
        var msg3 = await PushPassthroughRequestAsync(h, req3, cts.Token);

        Assert.Same(req3, msg3);
        // Verify no signal was emitted
        h.SignalProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ──── HTTP/1.1 Tests ────

    [Fact(Timeout = 5000, DisplayName = "EXT-RC-004: HTTP/1.1 first request emits ConnectItem")]
    public async Task HTTP11_FirstRequest_EmitsConnectItem()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var h = SetupHarness();
        var req = MakeRequest("http://example.com/h11", HttpVersion.Version11);

        var (signal, msg) = await PushFirstRequestAsync(h, req, cts.Token);

        Assert.Equal("example.com", signal.Options.Host);
        Assert.Same(req, msg);
    }

    [Fact(Timeout = 5000, DisplayName = "EXT-RC-005: HTTP/1.1 second request with Reuse skips ConnectItem")]
    public async Task HTTP11_SecondRequest_WithReuseTrue_SkipsConnectItem()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var h = SetupHarness();

        // First request
        var req1 = MakeRequest("http://example.com/1", HttpVersion.Version11);
        await PushFirstRequestAsync(h, req1, cts.Token);

        // Reuse feedback: KeepAlive (HTTP/1.1 default)
        h.SendReuse(MakeReuseItem(canReuse: true));

        // Second request → no reconnect
        var req2 = MakeRequest("http://example.com/2", HttpVersion.Version11);
        var msg2 = await PushPassthroughRequestAsync(h, req2, cts.Token);

        Assert.Same(req2, msg2);
        h.SignalProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
    }

    // ──── HTTP/2 Tests ────

    [Fact(Timeout = 5000, DisplayName = "EXT-RC-006: HTTP/2 first request emits ConnectItem")]
    public async Task HTTP20_FirstRequest_EmitsConnectItem()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var h = SetupHarness();
        var req = MakeRequest("http://example.com/h2", HttpVersion.Version20);

        var (signal, msg) = await PushFirstRequestAsync(h, req, cts.Token);

        Assert.Equal("example.com", signal.Options.Host);
        Assert.Same(req, msg);
    }

    /// <summary>
    /// The stage is protocol-agnostic: it does not inspect <c>HttpVersion</c>.
    /// In production, <see cref="ConnectionReuseEvaluator"/> always returns <c>CanReuse = true</c>
    /// for HTTP/2 (RFC 9113 §5.1: stream close ≠ connection close), so <c>_needsReconnect</c>
    /// is never set. This test verifies the stage behaviour when <c>CanReuse = false</c>
    /// is synthetically sent (which should not happen in practice for HTTP/2).
    ///
    /// Because the stage has no version check, it treats Close the same way for all versions.
    /// The test verifies this protocol-agnostic behaviour: Close → reconnect is triggered.
    /// </summary>
    [Fact(Timeout = 5000, DisplayName = "EXT-RC-007: HTTP/2 stage is protocol-agnostic — Close signal still triggers reconnect")]
    public async Task HTTP20_SecondRequest_WithReuseFalse_StageIsProtocolAgnostic()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var h = SetupHarness();

        // First request
        var req1 = MakeRequest("http://example.com/1", HttpVersion.Version20);
        await PushFirstRequestAsync(h, req1, cts.Token);

        // Reuse feedback: Close (would not occur in practice for HTTP/2,
        // but tests the stage's protocol-agnostic state machine)
        h.SendReuse(MakeReuseItem(canReuse: false));

        // Second request → stage emits ConnectItem because it is version-agnostic.
        // In production, ConnectionReuseEvaluator never sends Close for HTTP/2.
        var req2 = MakeRequest("http://example.com/2", HttpVersion.Version20);
        var (signal2, msg2) = await PushReconnectRequestAsync(h, req2, cts.Token);

        Assert.IsType<ConnectItem>(signal2);
        Assert.Same(req2, msg2);
    }

    // ──── Multi-hop Redirect Chain ────

    [Fact(Timeout = 5000, DisplayName = "EXT-RC-008: 3-hop redirect chain emits ConnectItem per Close")]
    public async Task MultipleRedirects_EmitsConnectItemPerClose()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var h = SetupHarness();
        var connectItems = new List<ConnectItem>();

        // Hop 1: first request → ConnectItem
        var req1 = MakeRequest("http://hop1.example.com/", HttpVersion.Version10);
        var (s1, _) = await PushFirstRequestAsync(h, req1, cts.Token);
        connectItems.Add(s1);

        // Connection closed after response (HTTP/1.0 default)
        h.SendReuse(MakeReuseItem(canReuse: false));

        // Hop 2: redirect → new ConnectItem
        var req2 = MakeRequest("http://hop2.example.com/", HttpVersion.Version10);
        var (s2, _) = await PushReconnectRequestAsync(h, req2, cts.Token);
        connectItems.Add(s2);

        // Connection closed again
        h.SendReuse(MakeReuseItem(canReuse: false));

        // Hop 3: redirect → new ConnectItem
        var req3 = MakeRequest("http://hop3.example.com/", HttpVersion.Version10);
        var (s3, _) = await PushReconnectRequestAsync(h, req3, cts.Token);
        connectItems.Add(s3);

        // Verify: 3 ConnectItems for 3 hops, each targeting the correct host
        Assert.Equal(3, connectItems.Count);
        Assert.Equal("hop1.example.com", connectItems[0].Options.Host);
        Assert.Equal("hop2.example.com", connectItems[1].Options.Host);
        Assert.Equal("hop3.example.com", connectItems[2].Options.Host);
    }

    // ──── Harness ────

    private sealed record StageHarness(
        Action<HttpRequestMessage> SendRequest,
        Action<IControlItem> SendReuse,
        Action<long> RequestSignalDemand,
        Action<long> RequestOutputDemand,
        TestSubscriber.ManualProbe<IOutputItem> SignalProbe,
        TestSubscriber.ManualProbe<HttpRequestMessage> RequestProbe);
}
