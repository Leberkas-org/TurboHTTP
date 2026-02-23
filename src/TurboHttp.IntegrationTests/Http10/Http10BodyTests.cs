using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// Phase 12 — HTTP/1.0 Integration Tests: POST /echo body scenarios.
/// Verifies that request bodies survive the encoder → server → decoder round-trip intact.
/// </summary>
[Collection("Http10Integration")]
public sealed class Http10BodyTests
{
    private readonly KestrelFixture _fixture;

    public Http10BodyTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<HttpResponseMessage> PostEchoAsync(byte[] body, string contentType = "application/octet-stream")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{_fixture.Port}/echo"))
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return Http10Helper.SendAsync(_fixture.Port, request);
    }

    private Task<HttpResponseMessage> PostEchoTextAsync(string text, string contentType = "text/plain")
    {
        var body = Encoding.UTF8.GetBytes(text);
        return PostEchoAsync(body, contentType);
    }

    // ── Small body ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-020: POST /echo small body is echoed correctly")]
    public async Task Post_Echo_SmallBody_IsEchoedCorrectly()
    {
        const string text = "hello echo";

        var response = await PostEchoTextAsync(text);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(text, body);
    }

    // ── 1 KB body ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-021: POST /echo 1 KB body is echoed correctly")]
    public async Task Post_Echo_1KbBody_IsEchoedCorrectly()
    {
        var body = new byte[1024];
        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)(i % 256);
        }

        var response = await PostEchoAsync(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoedBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, echoedBody);
    }

    // ── 64 KB body ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-022: POST /echo 64 KB body is echoed correctly")]
    public async Task Post_Echo_64KbBody_IsEchoedCorrectly()
    {
        var body = new byte[64 * 1024];
        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)(i % 256);
        }

        var response = await PostEchoAsync(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoedBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, echoedBody);
    }

    // ── Empty body ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-023: POST /echo empty body returns 200 with empty body")]
    public async Task Post_Echo_EmptyBody_Returns200_EmptyBody()
    {
        var response = await PostEchoAsync(Array.Empty<byte>());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoedBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(echoedBody);
    }

    // ── Binary body 0x00..0xFF ────────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-024: POST /echo binary body 0x00-0xFF is byte-accurate round-trip")]
    public async Task Post_Echo_BinaryBody_0x00To0xFF_ByteAccurateRoundTrip()
    {
        var body = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            body[i] = (byte)i;
        }

        var response = await PostEchoAsync(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoedBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, echoedBody);
    }

    // ── Body with CRLF ────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-025: POST /echo body containing CRLF is preserved")]
    public async Task Post_Echo_BodyWithCrlf_IsPreserved()
    {
        const string text = "line1\r\nline2\r\nline3";
        var body = Encoding.ASCII.GetBytes(text);

        var response = await PostEchoAsync(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoedBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, echoedBody);
    }

    // ── Body with null bytes ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-026: POST /echo body with null bytes is not truncated")]
    public async Task Post_Echo_BodyWithNullBytes_IsNotTruncated()
    {
        var body = "A\0B\0C"u8.ToArray(); // A\0B\0C

        var response = await PostEchoAsync(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoedBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, echoedBody);
    }

    // ── Content-Length accuracy ───────────────────────────────────────────────

    [Theory(DisplayName = "IT-10-027: POST /echo Content-Length header matches actual body byte count")]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(512)]
    [InlineData(1000)]
    public async Task Post_Echo_ContentLength_MatchesActualBodyLength(int size)
    {
        var body = new byte[size];
        Array.Fill(body, (byte)'X');

        var response = await PostEchoAsync(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoedBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(size, echoedBody.Length);
        Assert.Equal((long)size, response.Content.Headers.ContentLength);
    }

    // ── Content-Type mirroring ────────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-028: POST /echo Content-Type text/plain is mirrored in response")]
    public async Task Post_Echo_ContentType_TextPlain_IsMirrored()
    {
        var response = await PostEchoTextAsync("some text");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ct = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("text/plain", ct);
    }

    [Fact(DisplayName = "IT-10-029: POST /echo Content-Type application/json is mirrored in response")]
    public async Task Post_Echo_ContentType_Json_IsMirrored()
    {
        const string json = "{\"key\":\"value\"}";
        var response = await PostEchoTextAsync(json, "application/json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ct = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("application/json", ct);
    }

    // ── All-zeroes body ───────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-10-030: POST /echo all-zeroes body is preserved verbatim")]
    public async Task Post_Echo_AllZeroesBody_IsPreserved()
    {
        var body = new byte[128];
        // body is already all-zeroes

        var response = await PostEchoAsync(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoedBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(body, echoedBody);
    }
}
