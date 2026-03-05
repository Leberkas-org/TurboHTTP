using System.Net;
using System.Text;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http10;

public sealed class Http10WireComplianceTests : EngineTestBase
{
    private static readonly Func<byte[]> Http10OkEmpty =
        () => "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static Http10Engine Engine => new();

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§5.1: ST-10-WIRE-001: GET /path HTTP/1.0 CRLF exact bytes")]
    public async Task ST_10_WIRE_001_RequestLine_ExactBytes()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = HttpVersion.Version10
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10OkEmpty);

        Assert.StartsWith("GET /path HTTP/1.0\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§7.1: ST-10-WIRE-002: Header folding absent — each header on its own line")]
    public async Task ST_10_WIRE_002_NoHeaderFolding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10OkEmpty);

        Assert.DoesNotContain("\r\n ", raw);
        Assert.DoesNotContain("\r\n\t", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§5.1: ST-10-WIRE-003: Query string included in Request-URI")]
    public async Task ST_10_WIRE_003_QueryString_PreservedInRequestUri()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?foo=bar")
        {
            Version = HttpVersion.Version10
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10OkEmpty);

        Assert.StartsWith("GET /search?foo=bar HTTP/1.0\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§D.1: ST-10-WIRE-004: Wire target is path+query only, not scheme or host")]
    public async Task ST_10_WIRE_004_RequestTarget_IsPathOnly_NotAbsoluteUri()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource")
        {
            Version = HttpVersion.Version10
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10OkEmpty);

        var firstLine = raw[..raw.IndexOf("\r\n", StringComparison.Ordinal)];
        var requestTarget = firstLine.Split(' ')[1];

        Assert.StartsWith("/", requestTarget);
        Assert.DoesNotContain("http://", requestTarget);
        Assert.DoesNotContain("example.com", requestTarget);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§7.2: ST-10-WIRE-005: Content-Length matches actual body byte count")]
    public async Task ST_10_WIRE_005_ContentLength_MatchesBodyByteCount()
    {
        var body = Encoding.UTF8.GetBytes("hello world");
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version10,
            Content = new ByteArrayContent(body)
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10OkEmpty);

        Assert.Contains($"Content-Length: {body.Length}", raw);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIndex >= 0, "Missing double-CRLF header/body separator");

        var bodyPart = raw[(separatorIndex + 4)..];
        Assert.Equal(body.Length, Encoding.Latin1.GetByteCount(bodyPart));
    }
}
