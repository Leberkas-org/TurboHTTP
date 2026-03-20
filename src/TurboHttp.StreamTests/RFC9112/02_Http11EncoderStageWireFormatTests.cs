using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9112;

/// <summary>
/// RFC-tagged tests for the HTTP/1.1 request encoder stage per RFC 9112.
/// Verifies header folding, Host header presence, Content-Length, and chunked transfer encoding as mandated.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11EncoderStage"/>.
/// RFC 9112 §3–§6: HTTP/1.1 request-line, header fields, and transfer coding rules.
/// </remarks>
public sealed class Http11EncoderStageWireFormatTests : StreamTestBase
{
    private async Task<string> EncodeAsync(HttpRequestMessage request)
    {
        var items = await Source.Single(request)
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var sb = new StringBuilder();
        foreach (var item in items)
        {
            var data = (DataItem)item;
            sb.Append(Encoding.Latin1.GetString(data.Memory.Memory.Span[..data.Length]));
            data.Memory.Dispose();
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-3.1-11ES-001: Request-line format GET /path HTTP/1.1 CRLF")]
    public async Task Should_FormatRequestLine_WhenHttp11GetRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        var (requestLine, _, _) = Parse(raw);
        Assert.Equal("GET /path HTTP/1.1", requestLine);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-3.2-11ES-002: Host header MUST be present")]
    public async Task Should_IncludeHostHeader_WhenHttp11Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.Contains("Host:", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-3.2-11ES-003: Host header value equals URI authority (host:port)")]
    public async Task Should_SetHostHeaderToUriAuthority_WhenNonDefaultPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/resource")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.Contains("Host: example.com:8080\r\n", raw);
    }

    [Theory(Timeout = 10_000, DisplayName = "RFC9112-3.2-11ES-003b: Host header omits default port")]
    [InlineData("http://example.com/", "Host: example.com\r\n")]
    [InlineData("https://example.com/", "Host: example.com\r\n")]
    public async Task Should_OmitDefaultPortFromHostHeader_WhenUriUsesDefaultPort(string uri, string expectedHost)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.Contains(expectedHost, raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-6-11ES-004: POST with body includes Content-Length or Transfer-Encoding chunked")]
    public async Task Should_IncludeContentLengthOrChunked_WhenPostWithBody()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-5-11ES-005: Hop-by-hop headers (TE, Keep-Alive, Proxy-Connection) are stripped")]
    public async Task Should_StripHopByHopHeaders_WhenHttp11Request()
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
