using System.Net;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Tests.RFC9114;

/// <summary>
/// RFC 9114 §4.4 — CONNECT method handling.
/// Verifies that CONNECT requests use only :method and :authority pseudo-headers
/// (no :scheme, :path), and that successful CONNECT responses establish a tunnel
/// with no HTTP body.
/// </summary>
public sealed class ConnectMethodTests
{
    // ───────────────────────── Encoder: CONNECT Pseudo-Headers ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.4-con-001: CONNECT request has only :method and :authority")]
    public void Connect_request_has_only_method_and_authority()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8443/");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);

        var decoded = DecodeQpackBlock(headersFrame);
        AssertHasPseudoHeader(decoded, ":method", "CONNECT");
        AssertHasPseudoHeader(decoded, ":authority");
        AssertNoPseudoHeader(decoded, ":scheme");
        AssertNoPseudoHeader(decoded, ":path");
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-002: CONNECT :authority includes host and port")]
    public void Connect_authority_includes_host_and_port()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8443/");

        var frames = encoder.Encode(request);

        var decoded = DecodeQpackBlock(Assert.IsType<Http3HeadersFrame>(frames[0]));
        AssertHasPseudoHeader(decoded, ":authority", "proxy.example.com:8443");
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-003: CONNECT :authority always includes port for default HTTPS")]
    public void Connect_authority_includes_default_https_port()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://example.com/");

        var frames = encoder.Encode(request);

        var decoded = DecodeQpackBlock(Assert.IsType<Http3HeadersFrame>(frames[0]));
        // FormatAuthorityWithPort always includes port, even for default 443
        AssertHasPseudoHeader(decoded, ":authority", "example.com:443");
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-004: CONNECT :authority always includes port for default HTTP")]
    public void Connect_authority_includes_default_http_port()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "http://example.com/");

        var frames = encoder.Encode(request);

        var decoded = DecodeQpackBlock(Assert.IsType<Http3HeadersFrame>(frames[0]));
        AssertHasPseudoHeader(decoded, ":authority", "example.com:80");
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-005: CONNECT request with custom headers preserves them")]
    public void Connect_request_preserves_custom_headers()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8443/");
        request.Headers.TryAddWithoutValidation("proxy-authorization", "Basic dXNlcjpwYXNz");

        var frames = encoder.Encode(request);

        var decoded = DecodeQpackBlock(Assert.IsType<Http3HeadersFrame>(frames[0]));
        Assert.Contains(decoded, h => h.Name == "proxy-authorization" && h.Value == "Basic dXNlcjpwYXNz");
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-006: CONNECT produces single HEADERS frame (no DATA)")]
    public void Connect_produces_single_headers_frame()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8443/");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    // ───────────────────────── Validation: CONNECT Pseudo-Header Constraints ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.4-con-007: Validation rejects CONNECT with :scheme")]
    public void Validation_rejects_connect_with_scheme()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
            (":scheme", "https"),
            (":authority", "example.com:443"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(":scheme", ex.Message);
        Assert.Contains("4.4", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-008: Validation rejects CONNECT with :path")]
    public void Validation_rejects_connect_with_path()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
            (":path", "/"),
            (":authority", "example.com:443"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(":path", ex.Message);
        Assert.Contains("4.4", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-009: Validation rejects CONNECT with both :scheme and :path")]
    public void Validation_rejects_connect_with_scheme_and_path()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
            (":scheme", "https"),
            (":path", "/"),
            (":authority", "example.com:443"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        // Should reject :scheme first
        Assert.Contains(":scheme", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-010: Validation rejects CONNECT without :authority")]
    public void Validation_rejects_connect_without_authority()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(":authority", ex.Message);
        Assert.Contains("4.4", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-011: Validation accepts valid CONNECT with :method and :authority only")]
    public void Validation_accepts_valid_connect()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
            (":authority", "example.com:443"),
        };

        // Should not throw
        Http3RequestEncoder.ValidatePseudoHeaders(headers);
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-012: Validation accepts CONNECT with regular headers after pseudo-headers")]
    public void Validation_accepts_connect_with_regular_headers()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
            (":authority", "example.com:443"),
            ("proxy-authorization", "Basic dXNlcjpwYXNz"),
        };

        // Should not throw
        Http3RequestEncoder.ValidatePseudoHeaders(headers);
    }

    // ───────────────────────── Decoder: CONNECT Tunnel Response ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.4-con-013: 200 CONNECT response has no body (tunnel established)")]
    public async Task Successful_connect_response_has_no_body()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var frames = new List<Http3Frame>
        {
            EncodeResponseHeaders(200),
            new Http3DataFrame(new byte[] { 0x01, 0x02, 0x03 }), // tunnel data, not body
        };

        var response = decoder.Decode(frames, streamId: 0, isConnect: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Tunnel established — DATA frames are not assembled as body content
        var bodyLength = response.Content is null ? 0 : (await response.Content.ReadAsByteArrayAsync()).Length;
        Assert.Equal(0, bodyLength);
    }

    [Theory(DisplayName = "RFC-9114-4.4-con-014: 2xx CONNECT responses all establish tunnel")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    public async Task All_2xx_connect_responses_establish_tunnel(int statusCode)
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var frames = new List<Http3Frame>
        {
            EncodeResponseHeaders(statusCode),
            new Http3DataFrame(new byte[] { 0xAA, 0xBB }),
        };

        var response = decoder.Decode(frames, streamId: 0, isConnect: true);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
        // Tunnel data not assembled as body
        var bodyLength = response.Content is null ? 0 : (await response.Content.ReadAsByteArrayAsync()).Length;
        Assert.Equal(0, bodyLength);
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-015: Non-2xx CONNECT response includes body normally")]
    public async Task Non_2xx_connect_response_includes_body()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var bodyBytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var frames = new List<Http3Frame>
        {
            EncodeResponseHeaders(403),
            new Http3DataFrame(bodyBytes),
        };

        var response = decoder.Decode(frames, streamId: 0, isConnect: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(response.Content);
        var content = await response.Content!.ReadAsByteArrayAsync();
        Assert.Equal(bodyBytes, content);
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-016: 407 CONNECT response includes body")]
    public void Proxy_auth_required_connect_response_includes_body()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var frames = new List<Http3Frame>
        {
            EncodeResponseHeaders(407, [("proxy-authenticate", "Basic realm=\"proxy\"")]),
            new Http3DataFrame(new byte[] { 0x01 }),
        };

        var response = decoder.Decode(frames, streamId: 0, isConnect: true);

        Assert.Equal(HttpStatusCode.ProxyAuthenticationRequired, response.StatusCode);
        Assert.NotNull(response.Content);
        Assert.Equal("Basic realm=\"proxy\"", response.Headers.GetValues("proxy-authenticate").First());
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-017: 200 CONNECT with headers-only has no body")]
    public async Task Successful_connect_headers_only_has_no_body()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var frames = new List<Http3Frame>
        {
            EncodeResponseHeaders(200),
        };

        var response = decoder.Decode(frames, streamId: 0, isConnect: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bodyLength = response.Content is null ? 0 : (await response.Content.ReadAsByteArrayAsync()).Length;
        Assert.Equal(0, bodyLength);
    }

    [Fact(DisplayName = "RFC-9114-4.4-con-018: Non-CONNECT decode still assembles body normally")]
    public async Task Non_connect_decode_assembles_body_normally()
    {
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var bodyBytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        var frames = new List<Http3Frame>
        {
            EncodeResponseHeaders(200),
            new Http3DataFrame(bodyBytes),
        };

        var response = decoder.Decode(frames, streamId: 0, isConnect: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content);
        var content = await response.Content!.ReadAsByteArrayAsync();
        Assert.Equal(bodyBytes, content);
    }

    // ───────────────────────── Round-Trip: Encoder + Decoder ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.4-con-019: CONNECT round-trip encodes and decodes correctly")]
    public async Task Connect_round_trip()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8443/");

        var requestFrames = encoder.Encode(request);

        // Verify encoder output
        Assert.Single(requestFrames);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(requestFrames[0]);
        var decoded = DecodeQpackBlock(headersFrame);
        AssertHasPseudoHeader(decoded, ":method", "CONNECT");
        AssertHasPseudoHeader(decoded, ":authority", "proxy.example.com:8443");
        AssertNoPseudoHeader(decoded, ":scheme");
        AssertNoPseudoHeader(decoded, ":path");

        // Verify decoder handles 200 CONNECT as tunnel
        var decoder = new Http3ResponseDecoder(maxTableCapacity: 0);
        var responseFrames = new List<Http3Frame>
        {
            EncodeResponseHeaders(200),
        };
        var response = decoder.Decode(responseFrames, streamId: 0, isConnect: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bodyLength = response.Content is null ? 0 : (await response.Content.ReadAsByteArrayAsync()).Length;
        Assert.Equal(0, bodyLength);
    }

    // ───────────────────────── Helpers ─────────────────────────

    private static IReadOnlyList<(string Name, string Value)> DecodeQpackBlock(Http3HeadersFrame frame)
    {
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        return decoder.Decode(frame.HeaderBlock.Span, streamId: 0);
    }

    private static Http3HeadersFrame EncodeResponseHeaders(
        int statusCode,
        IReadOnlyList<(string Name, string Value)>? extraHeaders = null)
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", statusCode.ToString()),
        };
        if (extraHeaders is not null)
        {
            headers.AddRange(extraHeaders);
        }

        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var block = encoder.Encode(headers);
        return new Http3HeadersFrame(block);
    }

    private static void AssertHasPseudoHeader(
        IReadOnlyList<(string Name, string Value)> headers,
        string name,
        string? expectedValue = null)
    {
        var found = false;
        foreach (var (n, v) in headers)
        {
            if (n == name)
            {
                found = true;
                if (expectedValue is not null)
                {
                    Assert.Equal(expectedValue, v);
                }
                break;
            }
        }

        Assert.True(found, $"Expected pseudo-header '{name}' not found");
    }

    private static void AssertNoPseudoHeader(
        IReadOnlyList<(string Name, string Value)> headers,
        string name)
    {
        foreach (var (n, _) in headers)
        {
            if (n == name)
            {
                Assert.Fail($"Pseudo-header '{name}' should not be present in CONNECT request");
            }
        }
    }
}
