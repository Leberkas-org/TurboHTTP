using System.Net;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http3;

/// <summary>
/// RFC 9114 §4.4 — CONNECT method handling through the stream stage.
/// Verifies CONNECT request encoding (pseudo-header rules) and stage-level
/// response assembly for CONNECT responses.
/// </summary>
public sealed class Http30StreamStageConnectSpec : StreamTestBase
{
    private readonly QpackEncoder _qpack = new(maxTableCapacity: 0);

    private async Task<IReadOnlyList<HttpResponseMessage>> RunAsync(params Http3Frame[] frames)
    {
        return await Source.From(frames)
            .Via(Flow.FromGraph(new Http30StreamStage()))
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);
    }

    private ReadOnlyMemory<byte> EncodeHeaders(params (string Name, string Value)[] headers)
    {
        return _qpack.Encode(headers);
    }

    private Http3HeadersFrame EncodeResponseHeaders(
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

        return new Http3HeadersFrame(_qpack.Encode(headers));
    }

    // ── CONNECT request encoding ────────────────────────────────────────

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectRequest_should_have_only_method_and_authority_pseudo_headers()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectRequest_should_include_host_and_port_in_authority()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8443/");

        var frames = encoder.Encode(request);

        var decoded = DecodeQpackBlock(Assert.IsType<Http3HeadersFrame>(frames[0]));
        AssertHasPseudoHeader(decoded, ":authority", "proxy.example.com:8443");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectRequest_should_include_default_https_port_in_authority()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://example.com/");

        var frames = encoder.Encode(request);

        var decoded = DecodeQpackBlock(Assert.IsType<Http3HeadersFrame>(frames[0]));
        AssertHasPseudoHeader(decoded, ":authority", "example.com:443");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectRequest_should_include_default_http_port_in_authority()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "http://example.com/");

        var frames = encoder.Encode(request);

        var decoded = DecodeQpackBlock(Assert.IsType<Http3HeadersFrame>(frames[0]));
        AssertHasPseudoHeader(decoded, ":authority", "example.com:80");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectRequest_should_preserve_custom_headers()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8443/");
        request.Headers.TryAddWithoutValidation("proxy-authorization", "Basic dXNlcjpwYXNz");

        var frames = encoder.Encode(request);

        var decoded = DecodeQpackBlock(Assert.IsType<Http3HeadersFrame>(frames[0]));
        Assert.Contains(decoded, h => h is { Name: "proxy-authorization", Value: "Basic dXNlcjpwYXNz" });
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectRequest_should_produce_single_headers_frame()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8443/");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    // ── CONNECT pseudo-header validation ────────────────────────────────

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectValidation_should_reject_connect_with_scheme()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectValidation_should_reject_connect_with_path()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectValidation_should_reject_connect_with_scheme_and_path()
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
        Assert.Contains(":scheme", ex.Message);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectValidation_should_reject_connect_without_authority()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectValidation_should_accept_valid_connect()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
            (":authority", "example.com:443"),
        };

        Http3RequestEncoder.ValidatePseudoHeaders(headers);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ConnectValidation_should_accept_connect_with_regular_headers()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "CONNECT"),
            (":authority", "example.com:443"),
            ("proxy-authorization", "Basic dXNlcjpwYXNz"),
        };

        Http3RequestEncoder.ValidatePseudoHeaders(headers);
    }

    // ── CONNECT response stage tests ────────────────────────────────────

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public async Task Http30Stream_should_produce_connect_200_response_when_headers_only()
    {
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public async Task Http30Stream_should_produce_forbidden_response_with_body_when_connect_rejected()
    {
        var bodyBytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"

        var responses = await RunAsync(
            EncodeResponseHeaders(403),
            new Http3DataFrame(bodyBytes)
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.Forbidden, responses[0].StatusCode);
        Assert.NotNull(responses[0].Content);
        var content = await responses[0].Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyBytes, content);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public async Task Http30Stream_should_include_proxy_authenticate_header_when_407_response()
    {
        var responses = await RunAsync(
            EncodeResponseHeaders(407, [("proxy-authenticate", "Basic realm=\"proxy\"")])
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.ProxyAuthenticationRequired, responses[0].StatusCode);
        Assert.Equal("Basic realm=\"proxy\"",
            responses[0].Headers.GetValues("proxy-authenticate").First());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.4")]
    public async Task Http30Stream_should_produce_response_for_connect_round_trip()
    {
        // Verify encoder output
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://proxy.example.com:8443/");
        var requestFrames = encoder.Encode(request);

        Assert.Single(requestFrames);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(requestFrames[0]);
        var decoded = DecodeQpackBlock(headersFrame);
        AssertHasPseudoHeader(decoded, ":method", "CONNECT");
        AssertHasPseudoHeader(decoded, ":authority", "proxy.example.com:8443");
        AssertNoPseudoHeader(decoded, ":scheme");
        AssertNoPseudoHeader(decoded, ":path");

        // Verify stage handles 200 CONNECT response
        var headerBlock = EncodeHeaders(
            (":status", "200")
        );

        var responses = await RunAsync(
            new Http3HeadersFrame(headerBlock)
        );

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static IReadOnlyList<(string Name, string Value)> DecodeQpackBlock(Http3HeadersFrame frame)
    {
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        return decoder.Decode(frame.HeaderBlock.Span, streamId: 0);
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
