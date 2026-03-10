using System.Buffers;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http11;

/// <summary>
/// RFC 9112 §3.2 / §7.2 — Http11EncoderStage request-line and host header compliance tests.
/// Verifies that the stage produces correctly formatted HTTP/1.1 requests.
/// </summary>
public sealed class Http11EncoderStageRfcTests : StreamTestBase
{
    private async Task<string> EncodeAsync(HttpRequestMessage request)
    {
        var chunks = await Source.Single(request)
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .RunWith(Sink.Seq<(IMemoryOwner<byte>, int)>(), Materializer);

        var sb = new StringBuilder();
        foreach (var (owner, length) in chunks)
        {
            sb.Append(Encoding.Latin1.GetString(owner.Memory.Span[..length]));
            owner.Dispose();
        }

        return sb.ToString();
    }

    private static (string requestLine, string[] headerLines, string body) Parse(string raw)
    {
        var sep = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerSection = raw[..sep];
        var body = raw[(sep + 4)..];
        var lines = headerSection.Split("\r\n");
        return (lines[0], lines[1..], body);
    }

    [Fact(Timeout = 10_000, DisplayName = "11E-RFC-001: Request-line format: GET /path HTTP/1.1 CRLF")]
    public async Task _11E_RFC_001_RequestLine_Format()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        var (requestLine, _, _) = Parse(raw);
        Assert.Equal("GET /path HTTP/1.1", requestLine);
    }

    [Fact(Timeout = 10_000, DisplayName = "11E-RFC-002: Host header MUST be present (RFC 9112 §7.2)")]
    public async Task _11E_RFC_002_HostHeader_MustBePresent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.Contains("Host:", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "11E-RFC-003: Host header value equals URI authority (host:port)")]
    public async Task _11E_RFC_003_HostHeader_ValueEqualsAuthority()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/resource")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.Contains("Host: example.com:8080\r\n", raw);
    }

    [Theory(Timeout = 10_000, DisplayName = "11E-RFC-003: Host header omits default port")]
    [InlineData("http://example.com/", "Host: example.com\r\n")]
    [InlineData("https://example.com/", "Host: example.com\r\n")]
    public async Task _11E_RFC_003_HostHeader_OmitsDefaultPort(string uri, string expectedHost)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.Contains(expectedHost, raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "11E-RFC-004: POST with body includes Content-Length or Transfer-Encoding: chunked")]
    public async Task _11E_RFC_004_Post_ContentLengthOrChunked()
    {
        var body = "key=value"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = System.Net.HttpVersion.Version11,
            Content = new ByteArrayContent(body)
        };

        var raw = await EncodeAsync(request);

        var hasContentLength = raw.Contains("Content-Length:");
        var hasChunked = raw.Contains("Transfer-Encoding: chunked");
        Assert.True(hasContentLength || hasChunked,
            "POST with body must have Content-Length or Transfer-Encoding: chunked");
    }

    [Fact(Timeout = 10_000, DisplayName = "11E-RFC-005: Hop-by-hop headers (TE, Keep-Alive, Proxy-Connection) are stripped")]
    public async Task _11E_RFC_005_HopByHop_HeadersStripped()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = System.Net.HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("Proxy-Connection", "keep-alive");

        var raw = await EncodeAsync(request);

        Assert.DoesNotContain("TE:", raw);
        Assert.DoesNotContain("Keep-Alive:", raw);
        Assert.DoesNotContain("Proxy-Connection:", raw);
    }
}
