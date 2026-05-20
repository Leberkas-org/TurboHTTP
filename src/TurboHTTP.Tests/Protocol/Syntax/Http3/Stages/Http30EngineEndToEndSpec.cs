using TurboHTTP.Client;
using System.IO.Compression;
using System.Net;
using System.Text;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Stages;

public sealed class Http30EngineEndToEndSpec : EngineTestBase
{
    private static Http30ClientEngine Engine => new(new TurboClientOptions());

    private readonly QpackEncoder _qpack = new(maxTableCapacity: 0);

    private ReadOnlyMemory<byte> EncodeResponseHeaders(params (string Name, string Value)[] headers)
        => _qpack.Encode(headers);

    private static byte[] ServerSettings()
        => new SettingsFrame([]).Serialize();

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Engine_should_return_200_response_when_get_request_round_trips_with_settings_and_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/hello")
        {
            Version = HttpVersion.Version30
        };

        var headersFrame = new HeadersFrame(
            EncodeResponseHeaders((":status", "200"))).Serialize();

        var (response, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(outboundFrames, f => f is HeadersFrame);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Engine_should_emit_headers_and_data_frames_when_post_request_with_body_encoded()
    {
        const string payload = "field=value";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version30,
            Content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var headersFrame = new HeadersFrame(
            EncodeResponseHeaders((":status", "200"))).Serialize();

        var (response, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(outboundFrames, f => f is HeadersFrame);
        Assert.Contains(outboundFrames, f => f is DataFrame);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Engine_should_preserve_raw_compressed_body_when_content_encoding_is_gzip()
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

        var headersFrame = new HeadersFrame(
            EncodeResponseHeaders(
                (":status", "200"),
                ("content-encoding", "gzip"))).Serialize();

        var dataFrame = new DataFrame(compressedBody).Serialize();

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
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        // Engine preserves raw compressed bytes — decompression is handled by feature layer
        Assert.Equal(compressedBody, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public async Task Http30Engine_should_emit_settings_frame_when_engine_starts()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/settings-test")
        {
            Version = HttpVersion.Version30
        };

        var headersFrame = new HeadersFrame(
            EncodeResponseHeaders((":status", "200"))).Serialize();

        var (response, outboundFrames) = await SendH3EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(outboundFrames, f => f is SettingsFrame);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-4.1")]
    public async Task Http30Engine_should_preserve_body_content_when_response_has_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/body-test")
        {
            Version = HttpVersion.Version30
        };

        var expectedBody = "Response body content"u8.ToArray();

        var headersFrame = new HeadersFrame(
            EncodeResponseHeaders((":status", "200"))).Serialize();
        var dataFrame = new DataFrame(expectedBody).Serialize();

        var responseFrames = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(responseFrames, 0);
        dataFrame.CopyTo(responseFrames, headersFrame.Length);

        var (response, _) = await SendH3EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedBody, body);
    }
}
