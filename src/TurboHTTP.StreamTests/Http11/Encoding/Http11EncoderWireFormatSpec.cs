using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Encoding;
using TextEncoding = System.Text.Encoding;

namespace TurboHTTP.StreamTests.Http11.Encoding;

/// <summary>
/// RFC-tagged tests for the HTTP/1.1 request encoder stage per RFC 9112.
/// Verifies header folding, Host header presence, Content-Length, and chunked transfer encoding as mandated.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11EncoderStage"/>.
/// RFC 9112 §3–§6: HTTP/1.1 request-line, header fields, and transfer coding rules.
/// </remarks>
public sealed class Http11EncoderWireFormatSpec : StreamTestBase
{
    private async Task<string> EncodeAsync(HttpRequestMessage request)
    {
        var items = await Source.Single(request)
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var sb = new StringBuilder();
        foreach (var item in items)
        {
            var data = (NetworkBuffer)item;
            sb.Append(TextEncoding.Latin1.GetString(data.Span));
            data.Dispose();
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-3.1")]
    public async Task Http11Encoder_should_format_request_line_when_http11_get_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        var (requestLine, _, _) = Parse(raw);
        Assert.Equal("GET /path HTTP/1.1", requestLine);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-3.2")]
    public async Task Http11Encoder_should_include_host_header_when_http11_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.Contains("Host:", raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-3.2")]
    public async Task Http11Encoder_should_set_host_header_to_uri_authority_when_non_default_port()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/resource")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.Contains("Host: example.com:8080\r\n", raw);
    }

    [Theory(Timeout = 10_000)]
    [InlineData("http://example.com/", "Host: example.com\r\n")]
    [InlineData("https://example.com/", "Host: example.com\r\n")]
    [Trait("RFC", "RFC9112-3.2")]
    public async Task Http11Encoder_should_omit_default_port_from_host_header_when_uri_uses_default_port(string uri, string expectedHost)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.Contains(expectedHost, raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11Encoder_should_include_content_length_or_chunked_when_post_with_body()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-5")]
    public async Task Http11Encoder_should_strip_hop_by_hop_headers_when_http11_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = System.Net.HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("Proxy-Connection", "keep-alive");

        var raw = await EncodeAsync(request);

        // TE with non-chunked values is preserved per RFC 9112 §7.4 (listed in Connection)
        Assert.Contains("TE: trailers", raw);
        Assert.DoesNotContain("Keep-Alive:", raw);
        Assert.DoesNotContain("Proxy-Connection:", raw);
    }
}
