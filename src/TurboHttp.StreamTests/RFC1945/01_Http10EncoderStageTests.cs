using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.RFC1945;

/// <summary>
/// Tests the HTTP/1.0 request encoder stage per RFC 1945.
/// Verifies that request lines, headers, and bodies are correctly serialised to byte streams.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http10EncoderStage"/>.
/// RFC 1945 §5: HTTP/1.0 request message format and serialisation.
/// </remarks>
public sealed class Http10EncoderStageTests : StreamTestBase
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

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-5.1-10ES-001: Request-Line is METHOD SP path SP HTTP/1.0 CRLF")]
    public async Task Should_FormatRequestLine_When_GetRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/index.html")
        {
            Version = HttpVersion.Version10
        };

        var raw = await EncodeAsync(request);

        Assert.StartsWith("GET /index.html HTTP/1.0\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-7.1-10ES-002: Custom header is forwarded verbatim")]
    public async Task Should_ForwardCustomHeaderVerbatim_When_CustomHeaderSet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var raw = await EncodeAsync(request);

        Assert.Contains("X-Custom: value\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-D.1-10ES-003: No Host header emitted")]
    public async Task Should_NotEmitHostHeader_When_Http10Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var raw = await EncodeAsync(request);

        Assert.DoesNotContain("Host:", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-7.1-10ES-004: No Connection header emitted even when set on request")]
    public async Task Should_SuppressConnectionHeader_When_ConnectionHeaderSet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

        var raw = await EncodeAsync(request);

        Assert.DoesNotContain("Connection:", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-D.1-10ES-005: POST body bytes follow headers after double-CRLF")]
    public async Task Should_PlacePostBodyAfterHeaders_When_PostWithBody()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-D.1-10ES-006: Content-Length header present for POST body")]
    public async Task Should_IncludeContentLengthHeader_When_PostBodyPresent()
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

    [Fact(Timeout = 10_000,
        DisplayName = "RFC1945-D.1-10ES-007: Malformed request (null URI) → logged and dropped, stream encodes next request")]
    public async Task Should_DropMalformedRequestAndEncodeNextRequest_When_NullUriReceived()
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
        var data = (DataItem)item;
        try
        {
            var raw = Encoding.Latin1.GetString(data.Memory.Memory.Span[..data.Length]);
            Assert.StartsWith("GET /ok HTTP/1.0\r\n", raw);
        }
        finally
        {
            data.Memory.Dispose();
        }
    }
}
