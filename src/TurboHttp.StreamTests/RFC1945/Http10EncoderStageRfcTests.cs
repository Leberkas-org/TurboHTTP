using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC1945;

/// <summary>
/// RFC-tagged tests for the HTTP/1.0 request encoder stage per RFC 1945.
/// Verifies request-line format, header serialisation, and method handling as mandated by the specification.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http10EncoderStage"/>.
/// RFC 1945 §5: HTTP/1.0 request message format, request-line, and header fields.
/// </remarks>
public sealed class Http10EncoderStageRfcTests : StreamTestBase
{
    private async Task<string> EncodeAsync(HttpRequestMessage request)
    {
        var chunks = await Source.Single(request)
            .Via(Flow.FromGraph(new Http10EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var sb = new StringBuilder();
        foreach (var item in chunks)
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

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-5.1-10ES-001: Request-line format: GET /path HTTP/1.0 CRLF")]
    public async Task Should_FormatRequestLineCorrectly_When_GetRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var raw = await EncodeAsync(request);

        Assert.StartsWith("GET /path HTTP/1.0\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-7-10ES-002: POST with body includes Content-Length header")]
    public async Task Should_IncludeContentLength_When_PostWithBody()
    {
        var body = "hello=world"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new ByteArrayContent(body)
        };

        var raw = await EncodeAsync(request);

        Assert.Contains($"Content-Length: {body.Length}", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-5-10ES-003: No Host header in HTTP/1.0 request")]
    public async Task Should_NotEmitHostHeader_When_Http10Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var raw = await EncodeAsync(request);

        Assert.DoesNotContain("Host:", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-5.2-10ES-004: Connection header is not sent (no keep-alive in HTTP/1.0)")]
    public async Task Should_NotEmitConnectionHeader_When_Http10Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var raw = await EncodeAsync(request);

        Assert.DoesNotContain("Connection:", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-5.1-10ES-005: Query string is preserved in request target")]
    public async Task Should_PreserveQueryString_When_RequestTargetHasQuery()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?q=foo");

        var raw = await EncodeAsync(request);

        var (requestLine, _, _) = Parse(raw);
        Assert.Equal("GET /search?q=foo HTTP/1.0", requestLine);
    }
}
