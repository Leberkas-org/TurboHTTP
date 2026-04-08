using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Encoding;
using TextEncoding = System.Text.Encoding;

namespace TurboHTTP.StreamTests.Http11.Encoding;

/// <summary>
/// Tests the HTTP/1.1 request encoder stage per RFC 9112.
/// Verifies that request lines, headers, and chunked bodies are correctly serialised to byte streams.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11EncoderStage"/>.
/// RFC 9112 §3: HTTP/1.1 request message format, request-line, and header fields.
/// </remarks>
public sealed class Http11EncoderSpec : StreamTestBase
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-3.1")]
    public async Task Http11Encoder_should_format_request_line_when_http11_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/index.html")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.StartsWith("GET /index.html HTTP/1.1\r\n", raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-3.2")]
    public async Task Http11Encoder_should_emit_host_header_when_http11_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.Contains("Host: example.com\r\n", raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6.1")]
    public async Task Http11Encoder_should_include_framing_header_when_post_body_encoded()
    {
        var body = "hello"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = System.Net.HttpVersion.Version11,
            Content = new ByteArrayContent(body)
        };

        var raw = await EncodeAsync(request);

        var hasContentLength = raw.Contains("Content-Length:");
        var hasChunked = raw.Contains("Transfer-Encoding: chunked");
        Assert.True(hasContentLength || hasChunked, "Expected Content-Length or Transfer-Encoding: chunked framing header");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-5")]
    public async Task Http11Encoder_should_strip_hop_by_hop_headers_when_encoding()
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-3")]
    public async Task Http11Encoder_should_forward_custom_header_when_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = System.Net.HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var raw = await EncodeAsync(request);

        Assert.Contains("X-Custom: value\r\n", raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-3")]
    public async Task Http11Encoder_should_drop_malformed_request_and_encode_next_request_when_null_uri_received()
    {
        // A request with null RequestUri causes RequestEndpoint.FromRequest to throw.
        var malformed = new HttpRequestMessage { Method = HttpMethod.Get };
        var valid = new HttpRequestMessage(HttpMethod.Get, "http://example.com/ok")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var items = await Source.From([malformed, valid])
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        // Malformed request is dropped; only the valid request produces output.
        var item = Assert.Single(items);
        var data = (NetworkBuffer)item;
        try
        {
            var raw = TextEncoding.Latin1.GetString(data.Span);
            Assert.StartsWith("GET /ok HTTP/1.1\r\n", raw);
        }
        finally
        {
            data.Dispose();
        }
    }
}
