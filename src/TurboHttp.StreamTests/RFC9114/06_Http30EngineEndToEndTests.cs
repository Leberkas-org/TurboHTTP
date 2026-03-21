using System.IO.Compression;
using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Protocol.RFC9204;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// RFC-tagged round-trip tests for the HTTP/3 engine per RFC 9114.
/// Verifies end-to-end request encoding and response decoding through the full HTTP/3 protocol flow including QPACK.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30Engine"/>.
/// RFC 9114 §3–§7: HTTP/3 connection setup, frame exchange, and HTTP message mapping.
/// </remarks>
public sealed class Http30EngineEndToEndTests : EngineTestBase
{
    private static Http30Engine Engine => new();

    private readonly QpackEncoder _qpack = new(maxTableCapacity: 0);

    private ReadOnlyMemory<byte> EncodeResponseHeaders(params (string Name, string Value)[] headers)
        => _qpack.Encode(headers);

    private static byte[] ServerSettings()
        => new Http3SettingsFrame([]).Serialize();

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-ENG-001: GET → 200 with SETTINGS and HEADERS round-trip")]
    public async Task Should_Return200Response_When_GetRequestRoundTripsWithSettingsAndHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/hello")
        {
            Version = HttpVersion.Version30
        };

        var headersFrame = new Http3HeadersFrame(
            EncodeResponseHeaders((":status", "200"))).Serialize();

        var (response, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(outboundFrames, f => f is Http3HeadersFrame);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-ENG-002: POST with body → outbound has HEADERS + DATA frames")]
    public async Task Should_EmitHeadersAndDataFrames_When_PostRequestWithBodyEncoded()
    {
        const string payload = "field=value";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version30,
            Content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var headersFrame = new Http3HeadersFrame(
            EncodeResponseHeaders((":status", "200"))).Serialize();

        var (response, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(outboundFrames, f => f is Http3HeadersFrame);
        Assert.Contains(outboundFrames, f => f is Http3DataFrame);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-ENG-003: gzip-compressed response body is correctly decompressed")]
    public async Task Should_DecompressGzipResponseBody_When_ContentEncodingIsGzip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data")
        {
            Version = HttpVersion.Version30
        };

        var originalBody = "Hello, compressed HTTP/3 world!"u8.ToArray();
        byte[] compressedBody;
        using (var ms = new MemoryStream())
        {
            await using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(originalBody);
            }

            compressedBody = ms.ToArray();
        }

        var headersFrame = new Http3HeadersFrame(
            EncodeResponseHeaders(
                (":status", "200"),
                ("content-encoding", "gzip"))).Serialize();

        var dataFrame = new Http3DataFrame(compressedBody).Serialize();

        // Concatenate headers + data into a single server frame buffer
        var responseFrames = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(responseFrames, 0);
        dataFrame.CopyTo(responseFrames, headersFrame.Length);

        var (response, _) = await SendH3EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content!.ReadAsByteArrayAsync();
        Assert.Equal(originalBody, body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-ENG-004: SETTINGS frame emitted on engine start")]
    public async Task Should_EmitSettingsFrame_When_EngineStarts()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/settings-test")
        {
            Version = HttpVersion.Version30
        };

        var headersFrame = new Http3HeadersFrame(
            EncodeResponseHeaders((":status", "200"))).Serialize();

        var (response, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(outboundFrames, f => f is Http3SettingsFrame);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-ENG-005: Response with body preserves body content")]
    public async Task Should_PreserveBodyContent_When_ResponseHasBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/body-test")
        {
            Version = HttpVersion.Version30
        };

        var expectedBody = "Response body content"u8.ToArray();

        var headersFrame = new Http3HeadersFrame(
            EncodeResponseHeaders((":status", "200"))).Serialize();
        var dataFrame = new Http3DataFrame(expectedBody).Serialize();

        var responseFrames = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(responseFrames, 0);
        dataFrame.CopyTo(responseFrames, headersFrame.Length);

        var (response, _) = await SendH3EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content!.ReadAsByteArrayAsync();
        Assert.Equal(expectedBody, body);
    }
}
