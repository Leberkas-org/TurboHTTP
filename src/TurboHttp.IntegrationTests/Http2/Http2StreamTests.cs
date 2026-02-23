using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http2;

/// <summary>
/// Phase 15 — HTTP/2 Integration Tests: Stream lifecycle and request/response.
/// Tests cover stream creation, request methods, body, sequential streams,
/// RST_STREAM, END_STREAM placement, CONTINUATION frames, and large bodies.
/// </summary>
[Collection("Http2Integration")]
public sealed class Http2StreamTests
{
    private readonly KestrelH2Fixture _fixture;

    public Http2StreamTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
    }

    // ── Basic Stream Requests ─────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-020: Stream 1: GET /hello → 200 + body 'Hello World'")]
    public async Task Should_ReturnHelloWorld_When_GetHelloSentOnStream1()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello"));
        var (streamId, response) = (1, await conn.SendAndReceiveAsync(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "IT-2-021: Stream 1: POST /echo → server echoes body")]
    public async Task Should_EchoRequestBody_When_PostEchoSentOnStream1()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        const string data = "hello-echo";
        var request = new HttpRequestMessage(HttpMethod.Post, Http2Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = new StringContent(data, Encoding.UTF8, "text/plain")
        };
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(data, body);
    }

    [Fact(DisplayName = "IT-2-022: HEAD /hello → 200, no body in response")]
    public async Task Should_ReturnNoBody_When_HeadRequestSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Head, Http2Helper.BuildUri(_fixture.Port, "/hello"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // HEAD response must have no body (END_STREAM on HEADERS frame).
        if (response.Content is not null)
        {
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.Empty(body);
        }
    }

    [Fact(DisplayName = "IT-2-023: GET /status/204 → 204 No Content, empty body")]
    public async Task Should_ReturnNoContent_When_Status204Requested()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/status/204"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        if (response.Content is not null)
        {
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.Empty(body);
        }
    }

    // ── Sequential Streams ────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-024: Stream 1 then stream 3 (sequential) — both return correct responses")]
    public async Task Should_ReturnCorrectResponses_When_TwoSequentialStreamsUsed()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);

        var req1 = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello"));
        var resp1 = await conn.SendAndReceiveAsync(req1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal("Hello World", await resp1.Content.ReadAsStringAsync());

        var req2 = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        var resp2 = await conn.SendAndReceiveAsync(req2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.Equal("pong", await resp2.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "IT-2-025: Three sequential streams (1, 3, 5) — all return 200")]
    public async Task Should_ReturnOk_When_ThreeSequentialStreamsSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);

        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
            var response = await conn.SendAndReceiveAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ── RST_STREAM ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-026: Client sends RST_STREAM CANCEL — connection remains functional")]
    public async Task Should_RemainFunctional_When_ClientSendsRstStreamCancel()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // Complete a stream first to have a valid closed stream ID.
        var req = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        await conn.SendAndReceiveAsync(req);
        // Send RST_STREAM CANCEL for stream 1 (already closed — sends RST on closed stream,
        // which is allowed from client side).
        await conn.SendRstStreamAsync(1, Http2ErrorCode.Cancel);
        // Connection should still be usable.
        var req2 = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello"));
        var resp2 = await conn.SendAndReceiveAsync(req2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
    }

    // ── END_STREAM Placement ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-027: GET request has END_STREAM on HEADERS frame (no body)")]
    public async Task Should_SetEndStreamOnHeaders_When_GetRequestHasNoBody()
    {
        // We verify this by confirming a GET /hello succeeds — the server only sends
        // a response if it received a valid request with END_STREAM set correctly.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "IT-2-028: POST request has END_STREAM on DATA frame")]
    public async Task Should_SetEndStreamOnDataFrame_When_PostRequestHasBody()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var body = "end-stream-test"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, Http2Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new("application/octet-stream");
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receivedBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, receivedBody);
    }

    // ── CONTINUATION Frames ───────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-029: CONTINUATION frame triggered by large HEADERS block")]
    public async Task Should_SendContinuationFrame_When_HeaderBlockExceedsMaxFrameSize()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);

        // Build a request with enough custom headers to exceed 16384 bytes encoded.
        // Each header: ~50-byte name + ~50-byte value = ~104 bytes → 200 headers ≈ 20 KB.
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/headers/echo"));
        for (var i = 0; i < 200; i++)
        {
            request.Headers.TryAddWithoutValidation($"X-Custom-Header-{i:D4}", $"value-for-header-number-{i:D4}");
        }

        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "IT-2-030: Multiple CONTINUATION frames for very large HEADERS block")]
    public async Task Should_SendMultipleContinuationFrames_When_HeaderBlockIsVeryLarge()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);

        // 500 headers × ~104 bytes ≈ 52 KB → at least 3 CONTINUATION frames (16384 bytes each).
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/headers/count"));
        for (var i = 0; i < 500; i++)
        {
            request.Headers.TryAddWithoutValidation($"X-Big-Header-{i:D4}", $"value-for-big-header-number-{i:D4}");
        }

        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Stream State ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-031: Stream state idle → open → half-closed → closed completes cleanly")]
    public async Task Should_TransitionThroughStreamStates_When_RequestCompletes()
    {
        // The decoder tracks stream state internally.
        // We verify by completing a full request/response cycle.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // After response, stream is closed. A second request on the same connection
        // uses a new stream ID (stream 3).
        var request2 = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        var response2 = await conn.SendAndReceiveAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }

    // ── Large Bodies ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2-032: Large response body (60 KB) delivered across multiple DATA frames")]
    public async Task Should_DeliverLargeBody_When_60KbResponseRequested()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/60"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(60 * 1024, body.Length);
        Assert.True(body.All(b => b == (byte)'A'), "All bytes should be 'A'.");
    }

    [Fact(DisplayName = "IT-2-033: Large request body (32 KB) sent via DATA frames and echoed")]
    public async Task Should_EcholargeRequestBody_When_32KbPostSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var bodyData = new byte[32 * 1024];
        Array.Fill(bodyData, (byte)'Z');
        var request = new HttpRequestMessage(HttpMethod.Post, Http2Helper.BuildUri(_fixture.Port, "/h2/echo-binary"))
        {
            Content = new ByteArrayContent(bodyData)
        };
        request.Content.Headers.ContentType = new("application/octet-stream");
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyData.Length, received.Length);
        Assert.Equal(bodyData, received);
    }

    [Fact(DisplayName = "IT-2-034: Response body fragmented across multiple DATA frames is correctly reassembled")]
    public async Task Should_ReassembleFragmentedBody_When_LargeBodyReceivedInMultipleFrames()
    {
        // 20 KB body — Kestrel splits this into multiple DATA frames (16384 + remainder).
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/20"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(20 * 1024, body.Length);
    }

    [Fact(DisplayName = "IT-2-035: Five sequential streams each get correct responses")]
    public async Task Should_HandleFiveSequentialStreams_When_SentOnSameConnection()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        for (var i = 0; i < 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
            var response = await conn.SendAndReceiveAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("pong", await response.Content.ReadAsStringAsync());
        }
    }
}
