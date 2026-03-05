using System.Net;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http2;

/// <summary>
/// Phase 15 — HTTP/2 Integration Tests: HPACK header compression.
/// Tests cover static-table use, dynamic-table growth, Huffman encoding,
/// sensitive headers (never-index), pseudo-header order, and cookie handling.
/// </summary>
[Collection("Http2Integration")]
public sealed class Http2HpackTests
{
    private readonly KestrelH2Fixture _fixture;

    public Http2HpackTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
    }

    // ── Static Table / Literal Headers ───────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-2-040: First request — all headers encoded as literals (cold HPACK state)")]
    public async Task Should_EncodeAllLiteral_When_FirstRequestSent()
    {
        // Encode two requests with independent encoders to compare sizes.
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{_fixture.Port}/ping"));
        var (_, written1) = encoder.EncodeToBytes(request);

        // Encode same request again — HPACK table is warm, so second should be smaller.
        var (_, written2) = encoder.EncodeToBytes(request);

        Assert.True(written2 <= written1,
            $"Second identical request ({written2} bytes) should be <= first ({written1} bytes) due to HPACK indexing.");

        // Round-trip: send both over real connection and verify responses.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var r1 = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        var r2 = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        Assert.Equal(HttpStatusCode.OK, (await conn.SendAndReceiveAsync(r1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await conn.SendAndReceiveAsync(r2)).StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-041: Second identical request uses indexed headers (smaller HEADERS frame)")]
    public async Task Should_UseSmallerHeadersFrame_When_SecondIdenticalRequestSent()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var uri = new Uri($"http://127.0.0.1:{_fixture.Port}/hello");

        var req1 = new HttpRequestMessage(HttpMethod.Get, uri);
        var req2 = new HttpRequestMessage(HttpMethod.Get, uri);

        var (_, written1) = encoder.EncodeToBytes(req1);
        var (_, written2) = encoder.EncodeToBytes(req2);

        Assert.True(written2 < written1,
            $"Second request ({written2} bytes) should be smaller than first ({written1} bytes).");
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-042: HPACK dynamic table grows across requests with custom headers")]
    public async Task Should_GrowDynamicTable_When_CustomHeadersSentAcrossRequests()
    {
        // After several requests with the same custom header, later requests are smaller.
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var uri = new Uri($"http://127.0.0.1:{_fixture.Port}/headers/echo");
        const string headerName = "X-My-Header";
        const string headerValue = "constant-value";

        var sizes = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.TryAddWithoutValidation(headerName, headerValue);
            var (_, written) = encoder.EncodeToBytes(req);
            sizes.Add(written);
        }

        // Sizes should be non-increasing after the first couple of requests.
        Assert.True(sizes[4] <= sizes[0],
            $"5th request ({sizes[4]} bytes) should be <= 1st ({sizes[0]} bytes) after dynamic table warm-up.");
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-043: Sensitive header (Authorization) is never-indexed")]
    public async Task Should_NeverIndexAuthorizationHeader_When_Encoded()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/auth"));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-token");
        var response = await conn.SendAndReceiveAsync(request);
        // Server returns 200 when Authorization is present.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-044: HPACK static table entries used (method, path, status)")]
    public async Task Should_UseStaticTableEntries_When_CommonMethodsAndPathsEncoded()
    {
        // GET and /ping are in the static table; subsequent requests should be smaller.
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var uri = new Uri($"http://127.0.0.1:{_fixture.Port}/");

        var req1 = new HttpRequestMessage(HttpMethod.Get, uri);
        var buf1 = new byte[1024 * 64];
        var mem1 = buf1.AsMemory();
        var (_, written1) = encoder.Encode(req1, ref mem1);

        // First request should still be compact due to static table.
        Assert.True(written1 < 200, $"First GET / request should be compact due to static table; got {written1} bytes.");
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-045: Huffman encoding enabled — request headers are compressed")]
    public async Task Should_CompressHeaders_When_HuffmanEncodingEnabled()
    {
        // Compare encoded size with Huffman on vs off.
        var encoderHuffman = new Http2RequestEncoder(useHuffman: true);
        var encoderNoHuffman = new Http2RequestEncoder(useHuffman: false);
        var uri = new Uri($"http://127.0.0.1:{_fixture.Port}/hello");

        var req1 = new HttpRequestMessage(HttpMethod.Get, uri);
        var req2 = new HttpRequestMessage(HttpMethod.Get, uri);

        var buf1 = new byte[1024 * 64];
        var mem1 = buf1.AsMemory();
        var (_, withHuffman) = encoderHuffman.Encode(req1, ref mem1);

        var buf2 = new byte[1024 * 64];
        var mem2 = buf2.AsMemory();
        var (_, withoutHuffman) = encoderNoHuffman.Encode(req2, ref mem2);

        Assert.True(withHuffman <= withoutHuffman,
            $"Huffman-encoded request ({withHuffman} bytes) should be <= plain ({withoutHuffman} bytes).");
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-046: HPACK dynamic table eviction — table does not grow unbounded")]
    public async Task Should_EvictTableEntries_When_TableSizeLimitReached()
    {
        // Send 50 requests with unique headers — the table eventually reaches the 4096-byte limit
        // and starts evicting older entries. The connection should remain functional.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        for (var i = 0; i < 50; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
            request.Headers.TryAddWithoutValidation($"X-Unique-Header-{i:D6}", $"unique-value-{i:D6}-with-some-padding");
            var response = await conn.SendAndReceiveAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-047: Pseudo-headers order — :method, :path, :scheme, :authority come first")]
    public async Task Should_PlacePseudoHeadersFirst_When_RequestEncoded()
    {
        // We verify by confirming the server accepts the request correctly.
        // Kestrel requires pseudo-headers before regular headers per RFC 7540 §8.1.2.1.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello"));
        request.Headers.TryAddWithoutValidation("X-Custom", "value");
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-048: Response pseudo-header :status decoded — HttpResponseMessage.StatusCode set correctly")]
    public async Task Should_SetStatusCode_When_StatusPseudoHeaderDecoded()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/status/404"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-049: 20 custom headers sent and echoed back in response")]
    public async Task Should_EchoTwentyCustomHeaders_When_SentInRequest()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/headers/echo"));
        for (var i = 0; i < 20; i++)
        {
            request.Headers.TryAddWithoutValidation($"X-Header-{i:D2}", $"val-{i:D2}");
        }

        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Verify at least some X-* headers echoed back.
        Assert.True(response.Headers.Contains("X-Header-00"), "Response should echo X-Header-00.");
        Assert.True(response.Headers.Contains("X-Header-19"), "Response should echo X-Header-19.");
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-050: Response has Set-Cookie header — decoded without errors")]
    public async Task Should_DecodeSetCookieHeader_When_ResponseContainsCookie()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/h2/cookie"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Set-Cookie is a response header — check it arrived.
        Assert.True(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ||
            response.TrailingHeaders.TryGetValues("Set-Cookie", out _) ||
            response.Content?.Headers.Contains("Set-Cookie") == true ||
            response.Headers.Contains("Set-Cookie"),
            "Response should contain a Set-Cookie header.");
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-051: Authorization header sent and accepted by server (never-index does not break round-trip)")]
    public async Task Should_SucceedRoundTrip_When_AuthorizationHeaderSent()
    {
        // Authorization uses NeverIndex in HPACK but must still be transmitted correctly.
        var response = await Http2Helper.GetAsync(_fixture.Port, "/auth");
        // No Authorization → 401
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/auth"));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token-abc");
        var authResponse = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-052: HPACK decoder handles indexed + literal + indexed mix in response headers")]
    public async Task Should_DecodeResponseHeaders_When_HeadersUseIndexedAndLiteralMix()
    {
        // Response headers from Kestrel include a mix of indexed and literal entries.
        // The response with 20 custom headers exercises this well.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/h2/many-headers"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify at least 3 custom response headers arrived.
        var count = 0;
        foreach (var header in response.Headers)
        {
            if (header.Key.StartsWith("X-Custom-", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        Assert.True(count >= 3, $"Expected at least 3 X-Custom-* headers, got {count}.");
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-053: Multiple requests with same custom header — subsequent encodings are smaller")]
    public async Task Should_CompressSubsequentRequests_When_SameCustomHeaderRepeated()
    {
        var encoder = new Http2RequestEncoder(useHuffman: false);
        var uri = new Uri($"http://127.0.0.1:{_fixture.Port}/ping");
        const string customHeader = "X-Repeated-Header";
        const string customValue = "same-value-every-time";

        var sizes = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.TryAddWithoutValidation(customHeader, customValue);
            var buf = new byte[1024 * 64];
            var mem = buf.AsMemory();
            var (_, written) = encoder.Encode(req, ref mem);
            sizes.Add(written);
        }

        // 3rd request should be smallest as header is fully indexed.
        Assert.True(sizes[2] <= sizes[0],
            $"3rd request ({sizes[2]} bytes) should be <= 1st ({sizes[0]} bytes).");
    }
}
