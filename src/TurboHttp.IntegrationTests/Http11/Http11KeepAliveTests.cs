using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Phase 13 — HTTP/1.1 Integration Tests: Keep-alive and pipelining scenarios.
/// HTTP/1.1 connections are persistent by default (RFC 9112 §9.3).
/// Tests use <see cref="Http11Connection"/> to reuse a single TCP connection.
/// </summary>
[Collection("Http11Integration")]
public sealed class Http11KeepAliveTests
{
    private readonly KestrelFixture _fixture;

    public Http11KeepAliveTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Sequential keep-alive requests ───────────────────────────────────────

    [Fact(DisplayName = "IT-11-030: 2 sequential requests on same connection succeed")]
    public async Task TwoSequentialRequests_SameConnection_Succeed()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        var r1 = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
        var r2 = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("pong", await r1.Content.ReadAsStringAsync());
        Assert.Equal("pong", await r2.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "IT-11-031: 5 sequential requests on same connection all succeed")]
    public async Task FiveSequentialRequests_SameConnection_AllSucceed()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        for (var i = 0; i < 5; i++)
        {
            var r = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("pong", await r.Content.ReadAsStringAsync());
        }
    }

    [Fact(DisplayName = "IT-11-032: 10 sequential requests on same connection all succeed")]
    public async Task TenSequentialRequests_SameConnection_AllSucceed()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        for (var i = 0; i < 10; i++)
        {
            var r = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello")));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await r.Content.ReadAsStringAsync();
            Assert.Equal("Hello World", body);
        }
    }

    // ── Server Connection:close terminates reuse ─────────────────────────────

    [Fact(DisplayName = "IT-11-033: Server Connection:close response is flagged on connection")]
    public async Task ServerConnectionClose_IsFlaggedOnConnection()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        var r = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/close")));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("closing", await r.Content.ReadAsStringAsync());
        Assert.True(conn.IsServerClosed, "Connection should be marked as server-closed");
    }

    // ── Request with Connection:close header ─────────────────────────────────

    [Fact(DisplayName = "IT-11-034: Request with Connection:close sends close directive to server")]
    public async Task Request_WithConnectionClose_ResponseReceived()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"));
        request.Headers.ConnectionClose = true;

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    // ── Mixed keep-alive + close ──────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-035: Mixed keep-alive then close — both responses decoded correctly")]
    public async Task Mixed_KeepAlive_ThenClose_BothResponsesDecoded()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        // First request: keep-alive (default)
        var r1 = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // Second request: request close
        var req2 = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"));
        req2.Headers.ConnectionClose = true;
        var r2 = await conn.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("Hello World", await r2.Content.ReadAsStringAsync());
    }

    // ── Decoder resets cleanly between requests ───────────────────────────────

    [Fact(DisplayName = "IT-11-036: Decoder resets cleanly between requests on same connection")]
    public async Task Decoder_ResetsCleanly_BetweenRequests()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        // First request returns a body
        var r1 = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/large/4")));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var b1 = await r1.Content.ReadAsByteArrayAsync();
        Assert.Equal(4 * 1024, b1.Length);

        // Second request also works correctly with decoder in fresh state
        var r2 = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("pong", await r2.Content.ReadAsStringAsync());
    }

    // ── Keep-alive with varying body sizes ────────────────────────────────────

    [Fact(DisplayName = "IT-11-037: Keep-alive with varying body sizes — decoder handles each correctly")]
    public async Task KeepAlive_VaryingBodySizes_AllDecodedCorrectly()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        foreach (var kb in new[] { 1, 8, 32, 4 })
        {
            var r = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, $"/large/{kb}")));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await r.Content.ReadAsByteArrayAsync();
            Assert.Equal(kb * 1024, body.Length);
        }
    }

    // ── Keep-alive: GET then POST then GET ────────────────────────────────────

    [Fact(DisplayName = "IT-11-038: Keep-alive GET then POST then GET on same connection")]
    public async Task KeepAlive_Get_Post_Get_SameConnection()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        // GET
        var r1 = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello")));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // POST
        var postReq = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = new StringContent("data", Encoding.UTF8, "text/plain")
        };
        var r2 = await conn.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("data", await r2.Content.ReadAsStringAsync());

        // GET again
        var r3 = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello")));
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        Assert.Equal("Hello World", await r3.Content.ReadAsStringAsync());
    }

    // ── Pipeline depth 2 ─────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-039: Pipeline depth 2 — two requests in flight, responses in order")]
    public async Task Pipeline_Depth2_ResponsesInOrder()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")),
            new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"))
        };

        var responses = await conn.PipelineAsync(requests);

        Assert.Equal(2, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal("pong", await responses[0].Content.ReadAsStringAsync());
        Assert.Equal("Hello World", await responses[1].Content.ReadAsStringAsync());
    }

    // ── Pipeline depth 5 ─────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-040: Pipeline depth 5 — five requests in flight, all responses received")]
    public async Task Pipeline_Depth5_AllResponsesReceived()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        var requests = Enumerable.Range(1, 5)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")))
            .ToList();

        var responses = await conn.PipelineAsync(requests);

        Assert.Equal(5, responses.Count);
        foreach (var r in responses)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("pong", await r.Content.ReadAsStringAsync());
        }
    }

    // ── Pipeline with mixed verbs ─────────────────────────────────────────────

    [Fact(DisplayName = "IT-11-041: Pipeline with mixed GET+POST verbs — responses arrive in order")]
    public async Task Pipeline_MixedVerbs_ResponsesInOrder()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        var postContent = new StringContent("pipeline-post", Encoding.UTF8, "text/plain");
        var postReq = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = postContent
        };

        var requests = new HttpRequestMessage[]
        {
            new(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")),
            postReq,
            new(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"))
        };

        var responses = await conn.PipelineAsync(requests);

        Assert.Equal(3, responses.Count);
        Assert.Equal("pong", await responses[0].Content.ReadAsStringAsync());
        Assert.Equal("pipeline-post", await responses[1].Content.ReadAsStringAsync());
        Assert.Equal("Hello World", await responses[2].Content.ReadAsStringAsync());
    }

    // ── Responses arrive in request order ────────────────────────────────────

    [Fact(DisplayName = "IT-11-042: Pipelined responses arrive in request order — verified by body content")]
    public async Task Pipeline_ResponsesInRequestOrder_VerifiedByBody()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        // Send requests for different sizes — body lengths identify which response is which
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/large/1")),
            new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/large/2")),
            new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/large/4"))
        };

        var responses = await conn.PipelineAsync(requests);

        Assert.Equal(3, responses.Count);
        Assert.Equal(1 * 1024, (await responses[0].Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(2 * 1024, (await responses[1].Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(4 * 1024, (await responses[2].Content.ReadAsByteArrayAsync()).Length);
    }

    // ── Keep-alive across 20 sequential requests ──────────────────────────────

    [Fact(DisplayName = "IT-11-043: 20 sequential GET /ping on same keep-alive connection all succeed")]
    public async Task TwentySequential_GetPing_SameConnection_AllSucceed()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        for (var i = 0; i < 20; i++)
        {
            var r = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
    }
}
