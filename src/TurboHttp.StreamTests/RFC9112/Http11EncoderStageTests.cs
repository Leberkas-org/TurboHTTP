using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9112;

public sealed class Http11EncoderStageTests : StreamTestBase
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-3.1-11ES-001: Request-Line is METHOD SP path SP HTTP/1.1 CRLF")]
    public async Task ST_11_ENC_001_RequestLine_Format()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/index.html")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.StartsWith("GET /index.html HTTP/1.1\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-3.2-11ES-002: Host header is emitted for HTTP/1.1 requests")]
    public async Task ST_11_ENC_002_HostHeader_Emitted()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var raw = await EncodeAsync(request);

        Assert.Contains("Host: example.com\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-6.1-11ES-003: POST with known body has Content-Length or Transfer-Encoding chunked")]
    public async Task ST_11_ENC_003_PostBody_HasFramingHeader()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-5-11ES-004: Hop-by-hop connection-specific headers are stripped from wire")]
    public async Task ST_11_ENC_004_HopByHop_Headers_Stripped()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-3-11ES-005: Custom request header forwarded verbatim")]
    public async Task ST_11_ENC_005_CustomHeader_Forwarded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = System.Net.HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var raw = await EncodeAsync(request);

        Assert.Contains("X-Custom: value\r\n", raw);
    }
}
