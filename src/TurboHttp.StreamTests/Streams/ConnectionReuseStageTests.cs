using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

public sealed class ConnectionReuseStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task<(IReadOnlyList<HttpResponseMessage> responses, IReadOnlyList<ConnectionReuseItem> decisions)>
        RunAsync(Version httpVersion, bool bodyFullyConsumed = true, params HttpResponseMessage[] responses)
    {
        // Set version on each response so the stage can read it
        foreach (var r in responses)
        {
            r.Version = httpVersion;
        }

        var probe0 = this.CreateManualSubscriberProbe<HttpResponseMessage>();
        var probe1 = this.CreateManualSubscriberProbe<IControlItem>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var stage = b.Add(new ConnectionReuseStage(bodyFullyConsumed));
            var src = b.Add(Source.From(responses));

            b.From(src).To(stage.In);
            b.From(stage.Out0).To(Sink.FromSubscriber(probe0));
            b.From(stage.Out1).To(Sink.FromSubscriber(probe1));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var sub0 = await probe0.ExpectSubscriptionAsync(CancellationToken.None);
        var sub1 = await probe1.ExpectSubscriptionAsync(CancellationToken.None);

        var resultResponses = new List<HttpResponseMessage>();
        var resultDecisions = new List<ConnectionReuseItem>();

        // Request from both outlets for each response.
        // Both must have demand before the stage pulls upstream.
        for (var i = 0; i < responses.Length; i++)
        {
            sub0.Request(1);
            sub1.Request(1);
            resultResponses.Add(await probe0.ExpectNextAsync(CancellationToken.None));
            var signal = await probe1.ExpectNextAsync(CancellationToken.None);
            resultDecisions.Add((ConnectionReuseItem)signal);
        }

        return (resultResponses, resultDecisions);
    }

    private static HttpResponseMessage MakeResponse(
        HttpStatusCode status = HttpStatusCode.OK,
        string? connectionHeader = null,
        string? keepAliveHeader = null)
    {
        var response = new HttpResponseMessage(status);
        if (connectionHeader is not null)
        {
            response.Headers.TryAddWithoutValidation("Connection", connectionHeader);
        }
        if (keepAliveHeader is not null)
        {
            response.Headers.TryAddWithoutValidation("Keep-Alive", keepAliveHeader);
        }
        return response;
    }

    // ── HTTP/2: always reuse ───────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-001: HTTP/2 → CanReuse = true (multiplexed streams)")]
    public async Task REUSE_001_Http2_AlwaysReuse()
    {
        var response = MakeResponse();

        var (results, decisions) = await RunAsync(HttpVersion.Version20, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.True(decision.Decision.CanReuse);
    }

    // ── HTTP/1.1: persistent by default ───────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-002: HTTP/1.1 no Connection header → CanReuse = true")]
    public async Task REUSE_002_Http11_NoConnectionHeader_Reuse()
    {
        var response = MakeResponse();

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.True(decision.Decision.CanReuse);
    }

    [Fact(Timeout = 10_000, DisplayName = "REUSE-003: HTTP/1.1 Connection: close → CanReuse = false")]
    public async Task REUSE_003_Http11_ConnectionClose_NoReuse()
    {
        var response = MakeResponse(connectionHeader: "close");

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.False(decision.Decision.CanReuse);
    }

    // ── HTTP/1.0: opt-in keep-alive ────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-004: HTTP/1.0 Connection: Keep-Alive → CanReuse = true")]
    public async Task REUSE_004_Http10_KeepAlive_Reuse()
    {
        var response = MakeResponse(connectionHeader: "Keep-Alive");

        var (results, decisions) = await RunAsync(HttpVersion.Version10, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.True(decision.Decision.CanReuse);
    }

    [Fact(Timeout = 10_000, DisplayName = "REUSE-005: HTTP/1.0 no Connection header → CanReuse = false (not persistent by default)")]
    public async Task REUSE_005_Http10_NoKeepAlive_NoReuse()
    {
        var response = MakeResponse();

        var (results, decisions) = await RunAsync(HttpVersion.Version10, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.False(decision.Decision.CanReuse);
    }

    // ── body not fully consumed ────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-006: bodyFullyConsumed = false → CanReuse = false")]
    public async Task REUSE_006_BodyNotConsumed_NoReuse()
    {
        var response = MakeResponse();

        var (results, decisions) = await RunAsync(HttpVersion.Version11, bodyFullyConsumed: false, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.False(decision.Decision.CanReuse);
    }

    // ── 101 Switching Protocols ────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-007: 101 Switching Protocols → CanReuse = false")]
    public async Task REUSE_007_SwitchingProtocols_NoReuse()
    {
        var response = MakeResponse(HttpStatusCode.SwitchingProtocols);

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.False(decision.Decision.CanReuse);
    }

    // ── response passes through unchanged ─────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-008: response object passes through the stage unchanged")]
    public async Task REUSE_008_Response_PassesThrough()
    {
        var response = MakeResponse(HttpStatusCode.Created);

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        var result = Assert.Single(results);
        Assert.Same(response, result);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
    }

    // ── multiple responses ─────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-009: multiple responses each produce one decision")]
    public async Task REUSE_009_MultipleResponses_EachDecision()
    {
        var resp1 = MakeResponse(); // 200 → reuse
        var resp2 = MakeResponse(connectionHeader: "close"); // close → no reuse
        var resp3 = MakeResponse(); // 200 → reuse

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, resp1, resp2, resp3);

        Assert.Equal(3, results.Count);
        Assert.Equal(3, decisions.Count);
        Assert.True(decisions[0].Decision.CanReuse);
        Assert.False(decisions[1].Decision.CanReuse);
        Assert.True(decisions[2].Decision.CanReuse);
    }

    // ── Keep-Alive parameters ──────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-010: HTTP/1.1 Keep-Alive timeout and max parsed into decision")]
    public async Task REUSE_010_Http11_KeepAliveParams_Parsed()
    {
        var response = MakeResponse(keepAliveHeader: "timeout=30, max=100");

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.True(decision.Decision.CanReuse);
        Assert.Equal(TimeSpan.FromSeconds(30), decision.Decision.KeepAliveTimeout);
        Assert.Equal(100, decision.Decision.MaxRequests);
    }
}
