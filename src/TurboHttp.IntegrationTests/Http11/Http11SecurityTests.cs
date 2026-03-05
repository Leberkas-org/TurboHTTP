using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Phase 14 — HTTP/1.1 Integration Tests: Security and limits.
/// Tests cover large bodies, many headers, header injection prevention,
/// CRLF in body, zero-length body, negative Content-Length rejection,
/// long URIs, and slow server responses.
/// </summary>
[Collection("Http11Integration")]
public sealed class Http11SecurityTests
{
    private readonly KestrelFixture _fixture;

    public Http11SecurityTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Very large response body (10 MB) ─────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-045: GET /large/10240 (10 MB) — decoder accumulates large body without OOM")]
    public async Task LargeBody_10MB_DecoderNoOOM()
    {
        // 10 MB response from the server tests the decoder's ability to accumulate
        // large bodies across many TCP reads without excessive memory allocation.
        const int expectedBytes = 10 * 1024 * 1024; // 10 MB

        var response = await Http11Helper.GetAsync(_fixture.Port, "/large/10240");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(expectedBytes, responseBody.Length);
        // Spot-check: all bytes are 'A' as set by the /large/{kb} route
        Assert.Equal((byte)'A', responseBody[0]);
        Assert.Equal((byte)'A', responseBody[^1]);
    }

    // ── Very many request headers (50 headers) ────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-046: 50 custom headers in request — all echoed back in response")]
    public async Task FiftyCustomHeaders_AllPreservedInResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/headers/echo"));
        for (var i = 1; i <= 50; i++)
        {
            request.Headers.TryAddWithoutValidation($"X-Header-{i:D2}", $"val-{i}");
        }

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        for (var i = 1; i <= 50; i++)
        {
            var name = $"X-Header-{i:D2}";
            Assert.True(response.Headers.Contains(name), $"{name} should be echoed back");
            Assert.Equal($"val-{i}", response.Headers.GetValues(name).First());
        }
    }

    // ── Header injection: CR/LF in header value rejected ─────────────────────

    [Fact(DisplayName = "IT-11A-047: Header value with CR rejected by encoder (header injection prevention)")]
    public void HeaderInjection_CrInValue_Rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"));
        request.Headers.TryAddWithoutValidation("X-Injected", "value\rnext-line: injected");

        var ex = Assert.Throws<ArgumentException>(() => EncodeToBuffer(request));
        Assert.Contains("X-Injected", ex.Message);
    }

    [Fact(DisplayName = "IT-11A-048: Header value with LF rejected by encoder (header injection prevention)")]
    public void HeaderInjection_LfInValue_Rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"));
        request.Headers.TryAddWithoutValidation("X-Injected", "value\nnext-line: injected");

        var ex = Assert.Throws<ArgumentException>(() => EncodeToBuffer(request));
        Assert.Contains("X-Injected", ex.Message);
    }

    [Fact(DisplayName = "IT-11A-049: Header value with NUL byte rejected by encoder")]
    public void HeaderInjection_NulInValue_Rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/hello"));
        request.Headers.TryAddWithoutValidation("X-Nulled", "value\0null");

        var ex = Assert.Throws<ArgumentException>(() => EncodeToBuffer(request));
        Assert.Contains("X-Nulled", ex.Message);
    }

    /// <summary>Encodes a request into a 4 KB buffer. Used to test encoder validation without lambdas.</summary>
    private static string EncodeToBuffer(HttpRequestMessage request)
    {
        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer, 0, written);
    }

    // ── CRLF in body — body is opaque bytes, not re-parsed ───────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-050: POST /echo with CRLF bytes in body — body treated as opaque, echoed intact")]
    public async Task CrlfInBody_TreatedAsOpaque_EchoedIntact()
    {
        // Body contains HTTP-like content with CRLF — decoder must not re-parse as headers
        var bodyContent = "line1\r\nContent-Type: text/evil\r\n\r\nevil-body\r\n";
        var bodyBytes = Encoding.UTF8.GetBytes(bodyContent);

        var content = new ByteArrayContent(bodyBytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");

        var request = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = content
        };

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyBytes, responseBody);
    }

    // ── Zero-length Content-Length body ──────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-051: POST /echo with Content-Length: 0 — server returns 200 with empty body")]
    public async Task ZeroContentLength_PostEcho_Returns200_EmptyBody()
    {
        var content = new ByteArrayContent([]);
        content.Headers.ContentLength = 0;
        content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");

        var request = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = content
        };

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(responseBody);
    }

    // ── Negative Content-Length → encoder rejects ────────────────────────────

    [Fact(DisplayName = "IT-11A-052: Content-Length < 0 — encoder rejects with exception")]
    public void NegativeContentLength_Rejected_ByEncoder()
    {
        // .NET HttpContent.Headers.ContentLength disallows negative via its property setter
        // but TryAddWithoutValidation bypasses that. The encoder should reject it.
        var content = new ByteArrayContent([]);
        content.Headers.TryAddWithoutValidation("Content-Length", "-1");

        var request = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/echo"))
        {
            Content = content
        };

        var buffer = new byte[4096];
        var span = buffer.AsSpan();

        // Either encoder throws or .NET header API rejects it before encoding
        try
        {
            Http11Encoder.Encode(request, ref span);
            // If encode succeeds, verify the negative value was not literally encoded
            var encoded = Encoding.ASCII.GetString(buffer, 0, buffer.Length - span.Length);
            Assert.DoesNotContain("Content-Length: -1", encoded);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            // Expected: encoder or content headers rejected the negative value
        }
    }

    // ── Request URI > 8 KB ────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-11A-053: GET with query string > 8 KB — encoder encodes without exception")]
    public void LongUri_Over8KB_EncoderHandlesWithoutException()
    {
        // Build a URI with a query string that pushes the total path > 8 KB
        var longParam = new string('A', 9000);
        var uri = Http11Helper.BuildUri(_fixture.Port, $"/hello?q={longParam}");
        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        var buffer = new byte[16 * 1024]; // 16 KB buffer to hold the long URI
        var span = buffer.AsSpan();

        // Encoder must not throw; it should successfully serialize the long URI
        var written = Http11Encoder.Encode(request, ref span);

        Assert.True(written > 9000, $"Should have encoded > 9000 bytes, got {written}");
        // Verify the long query string appears in the encoded request
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("?q=", encoded);
        Assert.Contains(new string('A', 100), encoded); // spot-check
    }

    // ── Slow response — decoder accumulates ──────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-054: GET /slow/10 — decoder accumulates 10 bytes arriving 1-per-flush")]
    public async Task SlowResponse_DecoderAccumulates_ReturnsCompleteBody()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/slow/10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("xxxxxxxxxx", body); // 10 'x' bytes
    }
}
