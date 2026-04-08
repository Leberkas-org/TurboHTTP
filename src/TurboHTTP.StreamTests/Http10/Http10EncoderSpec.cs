using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Encoding;

namespace TurboHTTP.StreamTests.Http10;

/// <summary>
/// Tests the HTTP/1.0 request encoder stage per RFC 1945.
/// Verifies that request lines, headers, and bodies are correctly serialised to byte streams.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http10EncoderStage"/>.
/// RFC 1945 §5: HTTP/1.0 request message format and serialisation.
/// </remarks>
public sealed class Http10EncoderSpec : StreamTestBase
{
    private async Task<string> EncodeAsync(HttpRequestMessage request)
    {
        var chunks = await Source.Single(request)
            .Via(Flow.FromGraph(new Http10EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var sb = new StringBuilder();
        foreach (var item in chunks)
        {
            var data = (NetworkBuffer)item;
            sb.Append(Encoding.Latin1.GetString(data.Span));
            data.Dispose();
        }

        return sb.ToString();
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Http10Encoder_should_format_request_line_when_get_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/index.html")
        {
            Version = HttpVersion.Version10
        };

        var raw = await EncodeAsync(request);

        Assert.StartsWith("GET /index.html HTTP/1.0\r\n", raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-7.1")]
    public async Task Http10Encoder_should_forward_custom_header_verbatim_when_header_set()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var raw = await EncodeAsync(request);

        Assert.Contains("X-Custom: value\r\n", raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-D.1")]
    public async Task Http10Encoder_should_not_emit_host_header_when_http10_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var raw = await EncodeAsync(request);

        Assert.DoesNotContain("Host:", raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-D.1")]
    public async Task Http10Encoder_should_place_post_body_after_headers_when_post_with_body()
    {
        var body = "hello"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version10,
            Content = new ByteArrayContent(body)
        };

        var raw = await EncodeAsync(request);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIndex >= 0, "Missing double-CRLF header/body separator");
        var bodyPart = raw[(separatorIndex + 4)..];
        Assert.Contains("hello", bodyPart);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-D.1")]
    public async Task Http10Encoder_should_include_content_length_header_when_post_body_present()
    {
        var body = "hello"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version10,
            Content = new ByteArrayContent(body)
        };

        var raw = await EncodeAsync(request);

        Assert.Contains($"Content-Length: {body.Length}", raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-D.1")]
    public async Task Http10Encoder_should_drop_malformed_request_and_encode_next_request_when_null_uri()
    {
        // A request with null RequestUri causes RequestEndpoint.FromRequest to throw.
        var malformed = new HttpRequestMessage { Method = HttpMethod.Get };
        var valid = new HttpRequestMessage(HttpMethod.Get, "http://example.com/ok")
        {
            Version = HttpVersion.Version10
        };

        var items = await Source.From([malformed, valid])
            .Via(Flow.FromGraph(new Http10EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        // Malformed request is dropped; only the valid request produces output.
        var item = Assert.Single(items);
        var data = (NetworkBuffer)item;
        try
        {
            var raw = Encoding.Latin1.GetString(data.Span);
            Assert.StartsWith("GET /ok HTTP/1.0\r\n", raw);
        }
        finally
        {
            data.Dispose();
        }
    }
}
