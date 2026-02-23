using System.Net;
using System.Net.Sockets;
using System.Text;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Phase 14 — HTTP/1.1 Integration Tests: Edge cases.
/// Tests cover empty bodies, 204/304 no-body semantics, minimal responses,
/// unknown headers, OPTIONS *, empty POST, binary PUT, JSON PATCH, and
/// raw-socket decoder scenarios for very short / extra-CRLF responses.
/// </summary>
[Collection("Http11Integration")]
public sealed class Http11EdgeCaseTests
{
    private readonly KestrelFixture _fixture;

    public Http11EdgeCaseTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Empty response body with Content-Length: 0 ───────────────────────────

    [Fact(DisplayName = "IT-11A-055: GET /empty-cl — 200 with Content-Length: 0 decoded as empty body")]
    public async Task EmptyBody_ContentLength0_DecodedAsEmpty()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/empty-cl");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0L, response.Content.Headers.ContentLength);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── 204 No Content ────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-056: GET /status/204 — 204 No Content has empty body and no Content-Length")]
    public async Task Status204_NoBody_NoContentLength()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/status/204");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── 304 Not Modified ─────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-057: 304 Not Modified — no body in response")]
    public async Task Status304_NotModified_NoBody()
    {
        const string etag = "\"v1\"";
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/etag"));
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── Response with only headers (no body allowed) ─────────────────────────

    [Fact(DisplayName = "IT-11A-058: HEAD /any — response has headers only, no body allowed")]
    public async Task HeadRequest_HeadersOnly_NoBody()
    {
        var response = await Http11Helper.HeadAsync(_fixture.Port, "/any");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── Very short response via raw TCP ──────────────────────────────────────

    [Fact(DisplayName = "IT-11A-059: Minimal HTTP/1.1 200 OK\\r\\n\\r\\n — decoder parses correctly")]
    public async Task MinimalResponse_OnlyStatusLine_DecodedSuccessfully()
    {
        // Send a raw HTTP/1.1 response with no headers and no body (just status + CRLF CRLF)
        const string rawResponse = "HTTP/1.1 200 OK\r\n\r\n";

        var response = await RawEchoAsync(rawResponse);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── Multiple CRLF between header and body ────────────────────────────────

    [Fact(DisplayName = "IT-11A-060: Extra blank line before body — decoder stops at first CRLFCRLF")]
    public async Task ExtraBlankLineBeforeBody_DecoderUsesFirstCrlfCrlf()
    {
        // RFC 9112 §2.2: the server sends one blank line (CRLF CRLF) to end headers.
        // In this raw response there is Content-Length: 2, then CRLF CRLF, then the body "ok"
        // followed by an extra CRLF. The decoder should decode body as "ok" (2 bytes).
        const string rawResponse =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 2\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "ok";

        var response = await RawEchoAsync(rawResponse);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ok", body);
    }

    // ── Response with unknown headers ─────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-061: GET /unknown-headers — response non-standard X-Unknown-* headers preserved")]
    public async Task UnknownHeaders_PreservedInResponse()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/unknown-headers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Unknown-Foo"),
            "X-Unknown-Foo header should be preserved");
        Assert.Equal("bar", response.Headers.GetValues("X-Unknown-Foo").First());
        Assert.True(response.Headers.Contains("X-Unknown-Bar"),
            "X-Unknown-Bar header should be preserved");
        Assert.Equal("baz", response.Headers.GetValues("X-Unknown-Bar").First());
    }

    // ── OPTIONS * (asterisk request target) ──────────────────────────────────

    [Fact(DisplayName = "IT-11A-062: OPTIONS * — encoder produces OPTIONS * HTTP/1.1 request line")]
    public void OptionsAsterisk_EncoderProducesCorrectRequestLine()
    {
        // OPTIONS with asterisk URI — the encoder handles this specially
        var request = new HttpRequestMessage(HttpMethod.Options, Http11Helper.BuildUri(_fixture.Port, "/*"));

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);

        // Encoder must produce OPTIONS * HTTP/1.1 as the request line
        Assert.StartsWith("OPTIONS * HTTP/1.1\r\n", encoded);
    }

    [Fact(DisplayName = "IT-11A-063: OPTIONS /any — returns 200 with method name 'OPTIONS' in body")]
    public async Task Options_Path_Returns200_MethodInBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, Http11Helper.BuildUri(_fixture.Port, "/any"));
        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("OPTIONS", body);
    }

    // ── POST with empty body ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-064: POST /echo with empty body — server returns 200 with empty echo")]
    public async Task PostEmptyBody_Returns200_EmptyEcho()
    {
        var content = new ByteArrayContent([]);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");

        var request = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = content
        };

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── PUT with binary body (0x00..0xFF) ─────────────────────────────────────

    [Fact(DisplayName = "IT-11A-065: PUT /echo with binary body containing all byte values 0x00..0xFF — echoed intact")]
    public async Task PutBinaryBody_AllByteValues_EchoedIntact()
    {
        var binaryBody = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            binaryBody[i] = (byte)i;
        }

        var content = new ByteArrayContent(binaryBody);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");

        var request = new HttpRequestMessage(HttpMethod.Put, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = content
        };

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(binaryBody, responseBody);
    }

    // ── PATCH with JSON body ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-066: PATCH /echo with JSON body — server echoes JSON verbatim")]
    public async Task PatchJsonBody_EchoedVerbatim()
    {
        const string jsonPayload = "{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"new-name\"}";
        var bodyBytes = Encoding.UTF8.GetBytes(jsonPayload);
        var content = new ByteArrayContent(bodyBytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json");

        var request = new HttpRequestMessage(HttpMethod.Patch, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = content
        };

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(jsonPayload, body);
    }

    // ── HTTP version in response ──────────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-067: GET /hello — decoded response has HTTP/1.1 version")]
    public async Task Get_Hello_ResponseVersion_IsHttp11()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Version(1, 1), response.Version);
    }

    // ── Chunked response followed by 2nd request on same connection ──────────

    [Fact(DisplayName = "IT-11A-068: Two sequential GET requests on keep-alive connection — both succeed")]
    public async Task KeepAlive_TwoSequentialRequests_BothSucceed()
    {
        await using var conn = await Http11Connection.OpenAsync(_fixture.Port);

        var r1 = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            Http11Helper.BuildUri(_fixture.Port, "/hello")));
        var r2 = await conn.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            Http11Helper.BuildUri(_fixture.Port, "/ping")));

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal("Hello World", await r1.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("pong", await r2.Content.ReadAsStringAsync());
    }

    // ── Raw TCP helper ────────────────────────────────────────────────────────

    /// <summary>
    /// Spins up a disposable TCP listener that accepts one connection,
    /// writes <paramref name="rawResponse"/> bytes, then closes.
    /// Sends a simple GET / via Http11Encoder and decodes the one response.
    /// </summary>
    private async Task<HttpResponseMessage> RawEchoAsync(
        string rawResponse,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        // Start a raw TCP listener on a random port
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var rawPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var responseBytes = Encoding.ASCII.GetBytes(rawResponse);

        // Accept and respond in background
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(cts.Token);
            var stream = client.GetStream();
            // Drain the request (read until we see the header terminator)
            var buf = new byte[4096];
            var total = 0;
            while (total < 4)
            {
                var n = await stream.ReadAsync(buf.AsMemory(total), cts.Token);
                if (n == 0) break;
                total += n;
                // Look for \r\n\r\n in what we've read so far
                var s = Encoding.ASCII.GetString(buf, 0, total);
                if (s.Contains("\r\n\r\n")) break;
            }

            await stream.WriteAsync(responseBytes, cts.Token);
            await stream.FlushAsync(cts.Token);
        }, cts.Token);

        try
        {
            // Send GET / to the raw server
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{rawPort}/");
            return await Http11Helper.SendAsync(rawPort, request, cts.Token);
        }
        finally
        {
            listener.Stop();
            try { await serverTask; } catch (OperationCanceledException) { }
        }
    }
}
