using System.Net;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Streams;

public sealed class ConnectionReuseStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task<(IReadOnlyList<HttpResponseMessage> responses, IReadOnlyList<ConnectionReuseDecision> decisions)>
        RunAsync(Version httpVersion, bool bodyFullyConsumed = true, params HttpResponseMessage[] responses)
    {
        var decisions = new List<ConnectionReuseDecision>();
        var stage = new ConnectionReuseStage(httpVersion, d => decisions.Add(d), bodyFullyConsumed);

        var results = await Source.From(responses)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        return (results, decisions);
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
        Assert.True(decision.CanReuse);
    }

    // ── HTTP/1.1: persistent by default ───────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-002: HTTP/1.1 no Connection header → CanReuse = true")]
    public async Task REUSE_002_Http11_NoConnectionHeader_Reuse()
    {
        var response = MakeResponse();

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 10_000, DisplayName = "REUSE-003: HTTP/1.1 Connection: close → CanReuse = false")]
    public async Task REUSE_003_Http11_ConnectionClose_NoReuse()
    {
        var response = MakeResponse(connectionHeader: "close");

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.False(decision.CanReuse);
    }

    // ── HTTP/1.0: opt-in keep-alive ────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-004: HTTP/1.0 Connection: Keep-Alive → CanReuse = true")]
    public async Task REUSE_004_Http10_KeepAlive_Reuse()
    {
        var response = MakeResponse(connectionHeader: "Keep-Alive");

        var (results, decisions) = await RunAsync(HttpVersion.Version10, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.True(decision.CanReuse);
    }

    [Fact(Timeout = 10_000, DisplayName = "REUSE-005: HTTP/1.0 no Connection header → CanReuse = false (not persistent by default)")]
    public async Task REUSE_005_Http10_NoKeepAlive_NoReuse()
    {
        var response = MakeResponse();

        var (results, decisions) = await RunAsync(HttpVersion.Version10, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.False(decision.CanReuse);
    }

    // ── body not fully consumed ────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-006: bodyFullyConsumed = false → CanReuse = false")]
    public async Task REUSE_006_BodyNotConsumed_NoReuse()
    {
        var response = MakeResponse();

        var (results, decisions) = await RunAsync(HttpVersion.Version11, bodyFullyConsumed: false, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.False(decision.CanReuse);
    }

    // ── 101 Switching Protocols ────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-007: 101 Switching Protocols → CanReuse = false")]
    public async Task REUSE_007_SwitchingProtocols_NoReuse()
    {
        var response = MakeResponse(HttpStatusCode.SwitchingProtocols);

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.False(decision.CanReuse);
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
        Assert.True(decisions[0].CanReuse);
        Assert.False(decisions[1].CanReuse);
        Assert.True(decisions[2].CanReuse);
    }

    // ── Keep-Alive parameters ──────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "REUSE-010: HTTP/1.1 Keep-Alive timeout and max parsed into decision")]
    public async Task REUSE_010_Http11_KeepAliveParams_Parsed()
    {
        var response = MakeResponse(keepAliveHeader: "timeout=30, max=100");

        var (results, decisions) = await RunAsync(HttpVersion.Version11, true, response);

        Assert.Single(results);
        var decision = Assert.Single(decisions);
        Assert.True(decision.CanReuse);
        Assert.Equal(TimeSpan.FromSeconds(30), decision.KeepAliveTimeout);
        Assert.Equal(100, decision.MaxRequests);
    }
}
