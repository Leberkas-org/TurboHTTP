using System.Buffers;
using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http10;

public sealed class Http10EncoderStageTests : StreamTestBase
{
    private async Task<string> EncodeAsync(HttpRequestMessage request)
    {
        var chunks = await Source.Single(request)
            .Via(Flow.FromGraph(new Http10EncoderStage()))
            .RunWith(Sink.Seq<(IMemoryOwner<byte>, int)>(), Materializer);

        var sb = new StringBuilder();
        foreach (var (owner, length) in chunks)
        {
            sb.Append(Encoding.Latin1.GetString(owner.Memory.Span[..length]));
            owner.Dispose();
        }

        return sb.ToString();
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§5.1: Request-Line is METHOD SP path SP HTTP/1.0 CRLF")]
    public async Task ST_10_ENC_001_RequestLine_Format()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/index.html")
        {
            Version = HttpVersion.Version10
        };

        var raw = await EncodeAsync(request);

        Assert.StartsWith("GET /index.html HTTP/1.0\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§7.1: Custom header is forwarded verbatim")]
    public async Task ST_10_ENC_002_CustomHeader_Forwarded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var raw = await EncodeAsync(request);

        Assert.Contains("X-Custom: value\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§D.1: No Host header emitted")]
    public async Task ST_10_ENC_003_NoHostHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var raw = await EncodeAsync(request);

        Assert.DoesNotContain("Host:", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§7.1: No Connection header emitted even when set on request")]
    public async Task ST_10_ENC_004_ConnectionHeader_Suppressed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

        var raw = await EncodeAsync(request);

        Assert.DoesNotContain("Connection:", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§D.1: POST body bytes follow headers after double-CRLF")]
    public async Task ST_10_ENC_005_PostBody_FollowsHeaders()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§D.1: Content-Length header present for POST body")]
    public async Task ST_10_ENC_006_ContentLength_PresentForPostBody()
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
}
