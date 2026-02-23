using System.Net;
using System.Net.Sockets;
using System.Text;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// Phase 12 — HTTP/1.0 Integration Tests: Connection lifecycle scenarios.
/// HTTP/1.0 default: connection closes after each response (no keep-alive).
/// </summary>
[Collection("Http10Integration")]
public sealed class Http10ConnectionTests
{
    private readonly KestrelFixture _fixture;

    public Http10ConnectionTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Connection closes after response ──────────────────────────────────────

    [Fact(DisplayName = "IT-10-080: Connection closes after HTTP/1.0 response — second read returns 0")]
    public async Task Connection_ClosesAfterResponse()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _fixture.Port);
        var stream = tcp.GetStream();

        // Encode and send a GET /hello HTTP/1.0 request
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{_fixture.Port}/hello"));
        var encBuf = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref encBuf);
        await stream.WriteAsync(encBuf[..written]);

        // Consume the full response
        var decoder = new Http10Decoder();
        var readBuf = new byte[65536];
        HttpResponseMessage? response = null;
        while (response is null)
        {
            var n = await stream.ReadAsync(readBuf);
            if (n == 0)
            {
                decoder.TryDecodeEof(out response);
                break;
            }

            decoder.TryDecode(readBuf.AsMemory(0, n), out response);
        }

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // After reading the full response the server should close the connection.
        // A subsequent read must eventually return 0 (EOF).
        stream.ReadTimeout = 3000;
        var extra = await stream.ReadAsync(readBuf);
        Assert.Equal(0, extra);
    }

    // ── Multiple sequential requests each need a new connection ───────────────

    [Fact(DisplayName = "IT-10-081: Five sequential GET /ping requests on separate connections all succeed")]
    public async Task FiveSequentialRequests_SeparateConnections_AllSucceed()
    {
        for (var i = 0; i < 5; i++)
        {
            var response = await Http10Helper.GetAsync(_fixture.Port, "/ping");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("pong", body);
        }
    }

    // ── TryDecodeEof on closed connection ─────────────────────────────────────

    [Fact(DisplayName = "IT-10-082: TryDecodeEof succeeds when server closes connection — HEAD response")]
    public async Task TryDecodeEof_SucceedsOnServerClose_HeadResponse()
    {
        // HEAD response has no body; decoder must handle server-close + TryDecodeEof
        var response = await Http10Helper.HeadAsync(_fixture.Port, "/hello");

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── Partial response → decoder returns false ──────────────────────────────

    [Fact(DisplayName = "IT-10-083: Partial response bytes cause TryDecode to return false until complete")]
    public async Task PartialResponse_TryDecode_ReturnsFalse_UntilComplete()
    {
        // Build a synthetic partial response and feed it to a fresh decoder
        var fullResponse = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();

        var decoder = new Http10Decoder();

        // Feed only the first 10 bytes (incomplete headers)
        var partial = fullResponse.AsMemory(0, 10);
        var result = decoder.TryDecode(partial, out var incomplete);

        Assert.False(result);
        Assert.Null(incomplete);

        // Feed the rest
        var rest = fullResponse.AsMemory(10);
        result = decoder.TryDecode(rest, out var complete);

        Assert.True(result);
        Assert.NotNull(complete);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var body = await complete.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    // ── Server-sent Connection:close handled correctly ────────────────────────

    [Fact(DisplayName = "IT-10-084: Response decoded successfully when server sends Connection: close")]
    public async Task Response_DecodedSuccessfully_WhenServerSendsConnectionClose()
    {
        // Kestrel sends Connection: close for HTTP/1.0 requests.
        // Our decoder must handle this without error.
        var response = await Http10Helper.GetAsync(_fixture.Port, "/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    // ── Two independent TCP clients, concurrent requests ──────────────────────

    [Fact(DisplayName = "IT-10-085: Two concurrent GET /ping requests on separate connections both succeed")]
    public async Task TwoConcurrent_GetPing_SeparateConnections_BothSucceed()
    {
        var t1 = Http10Helper.GetAsync(_fixture.Port, "/ping");
        var t2 = Http10Helper.GetAsync(_fixture.Port, "/ping");

        var results = await Task.WhenAll(t1, t2);

        Assert.Equal(HttpStatusCode.OK, results[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, results[1].StatusCode);
    }

    // ── Decoder Reset clears state ────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-086: Http10Decoder.Reset clears remainder so next TryDecode starts fresh")]
    public void Decoder_Reset_ClearsRemainder()
    {
        var partial = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n"u8.ToArray();
        var decoder = new Http10Decoder();

        var result = decoder.TryDecode(partial, out _);
        Assert.False(result); // incomplete

        decoder.Reset();

        // After reset, feeding the complete response should succeed
        var full = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();
        result = decoder.TryDecode(full, out var response);

        Assert.True(result);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
