using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.Http10;

/// <summary>
/// RFC-tagged tests for the HTTP/1.0 request encoder stage per RFC 1945.
/// Verifies request-line format, header serialisation, and method handling as mandated by the specification.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http10EncoderStage"/>.
/// RFC 1945 §5: HTTP/1.0 request message format, request-line, and header fields.
/// </remarks>
public sealed class Http10EncoderWireFormatSpec : StreamTestBase
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

    private static (string requestLine, string[] headerLines, string body) Parse(string raw)
    {
        var sep = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerSection = raw[..sep];
        var body = raw[(sep + 4)..];
        var lines = headerSection.Split("\r\n");
        return (lines[0], lines[1..], body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Http10EncoderWireFormat_should_format_request_line_correctly_when_get_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var raw = await EncodeAsync(request);

        Assert.StartsWith("GET /path HTTP/1.0\r\n", raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Http10EncoderWireFormat_should_include_content_length_when_post_with_body()
    {
        var body = "hello=world"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new ByteArrayContent(body)
        };

        var raw = await EncodeAsync(request);

        Assert.Contains($"Content-Length: {body.Length}", raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-5")]
    public async Task Http10EncoderWireFormat_should_not_emit_host_header_when_http10_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var raw = await EncodeAsync(request);

        Assert.DoesNotContain("Host:", raw);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Http10EncoderWireFormat_should_preserve_query_string_when_request_target_has_query()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?q=foo");

        var raw = await EncodeAsync(request);

        var (requestLine, _, _) = Parse(raw);
        Assert.Equal("GET /search?q=foo HTTP/1.0", requestLine);
    }
}
