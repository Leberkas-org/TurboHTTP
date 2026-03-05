using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http11;

public sealed class Http11WireComplianceTests : EngineTestBase
{
    private static readonly Func<byte[]> Http11OkEmpty =
        () => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static Http11Engine Engine => new();

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§3.1: ST-11-WIRE-001: GET /path HTTP/1.1 CRLF exact bytes")]
    public async Task ST_11_WIRE_001_RequestLine_ExactBytes()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = HttpVersion.Version11
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11OkEmpty);

        Assert.StartsWith("GET /path HTTP/1.1\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§7.2: ST-11-WIRE-002: Host header is present and correct")]
    public async Task ST_11_WIRE_002_HostHeader_PresentAndCorrect()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11OkEmpty);

        Assert.Contains("Host: example.com\r\n", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§7.6.1: ST-11-WIRE-003: Hop-by-hop Keep-Alive header absent on outbound")]
    public async Task ST_11_WIRE_003_KeepAlive_HopByHop_NotForwarded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5, max=100");

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11OkEmpty);

        Assert.DoesNotContain("Keep-Alive:", raw);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§6.1: ST-11-WIRE-004: Chunked encoding first chunk header is hex-size CRLF")]
    public async Task ST_11_WIRE_004_ChunkedEncoding_FirstChunkHeader_Format()
    {
        var bodyBytes = Encoding.UTF8.GetBytes("hello world");
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version11,
            Content = new ByteArrayContent(bodyBytes)
        };
        request.Headers.TransferEncodingChunked = true;

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11OkEmpty);

        Assert.Contains("Transfer-Encoding: chunked\r\n", raw);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIndex >= 0, "Missing header/body separator");

        var bodySection = raw[(separatorIndex + 4)..];
        Assert.Matches(new Regex(@"^[0-9a-fA-F]+\r\n"), bodySection);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9112-§2.1: ST-11-WIRE-005: Header section ends with double CRLF before body")]
    public async Task ST_11_WIRE_005_HeaderSection_EndsWith_DoubleCrlf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11OkEmpty);

        Assert.Contains("\r\n\r\n", raw);
    }

    [Theory(Timeout = 10_000, DisplayName = "RFC-9112-§3.1: ST-11-WIRE-006: Method preserved verbatim on outbound wire")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task ST_11_WIRE_006_Method_PreservedVerbatim(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "http://example.com/resource")
        {
            Version = HttpVersion.Version11
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11OkEmpty);

        Assert.StartsWith($"{method} /resource HTTP/1.1\r\n", raw);
    }
}
