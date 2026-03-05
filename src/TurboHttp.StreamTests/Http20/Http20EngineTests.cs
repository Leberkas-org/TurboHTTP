using System.Buffers;
using System.IO.Compression;
using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20EngineTests : EngineTestBase
{
    private static Http20Engine Engine => new();

    private static readonly byte[] ServerSettings = new SettingsFrame([]).Serialize();

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static byte[] BuildH2Response(int streamId, int status, string body = "")
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", status.ToString())]);
        var headersFrame = new HeadersFrame(streamId, headerBlock,
            endStream: body.Length == 0, endHeaders: true).Serialize();

        if (body.Length == 0)
        {
            return headersFrame;
        }

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var dataFrame = new DataFrame(streamId, bodyBytes, endStream: true).Serialize();
        var combined = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(combined, 0);
        dataFrame.CopyTo(combined, headersFrame.Length);
        return combined;
    }

    // ── ST-20-001 ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.1: ST-20-001: Simple GET returns 200")]
    public async Task Simple_GET_Returns_200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        };

        var (response, _) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── ST-20-002 ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3: ST-20-002: Request encodes HPACK pseudo-headers")]
    public async Task Request_Encodes_HPACK_Pseudo_Headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?q=1")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200));

        // Outbound (after preface) contains: SETTINGS ACK, request HEADERS.
        var headersFrame = outboundFrames.OfType<HeadersFrame>().FirstOrDefault();
        Assert.NotNull(headersFrame);

        var hpack = new HpackDecoder();
        var decoded = hpack.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.Contains(decoded, h => h.Name == ":method" && h.Value == "GET");
        Assert.Contains(decoded, h => h.Name == ":path" && h.Value == "/path?q=1");
        Assert.Contains(decoded, h => h.Name == ":scheme" && h.Value == "https");
        Assert.Contains(decoded, h => h.Name == ":authority" && h.Value == "example.com");
    }

    // ── ST-20-003 ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.1: ST-20-003: POST with body sends DATA frame after HEADERS")]
    public async Task POST_With_Body_Sends_DATA_Frame_After_HEADERS()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/submit")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent("hello=world", Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var (response, outboundFrames) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Outbound (after preface) contains: SETTINGS ACK, request HEADERS, request DATA.
        var headersFrame = outboundFrames.OfType<HeadersFrame>().FirstOrDefault();
        var dataFrame = outboundFrames.OfType<DataFrame>().FirstOrDefault();

        Assert.NotNull(headersFrame);
        Assert.NotNull(dataFrame);
        Assert.False(headersFrame.EndStream, "HEADERS must not have END_STREAM when body follows");

        // HEADERS must appear before DATA in the frame sequence.
        var frames = outboundFrames.ToList();
        Assert.True(frames.IndexOf(headersFrame) < frames.IndexOf(dataFrame),
            "HEADERS frame must precede DATA frame");
    }

    // ── ST-20-004 ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.1: ST-20-004: Response with body is decoded")]
    public async Task Response_With_Body_Is_Decoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        };

        var (response, _) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200, body: "hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    // ── ST-20-005 ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9110-§8.4: ST-20-005: Content-Encoding gzip response is decompressed")]
    public async Task Gzip_Response_Is_Decompressed()
    {
        const string originalBody = "hello compressed world";
        var gzipBody = GzipCompress(originalBody);

        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("content-encoding", "gzip")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, gzipBody, endStream: true).Serialize();
        var responseBytes = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(responseBytes, 0);
        dataFrame.CopyTo(responseBytes, headersFrame.Length);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        };

        var (response, _) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            responseBytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(originalBody, body);
    }

    private static byte[] GzipCompress(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress))
        {
            gz.Write(bytes, 0, bytes.Length);
        }

        return ms.ToArray();
    }

    // ── ST-20-006 ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§5.1.1: ST-20-006: Multiple concurrent streams processed in order")]
    public async Task Multiple_Streams_Processed_In_Order()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/1") { Version = HttpVersion.Version20 },
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/2") { Version = HttpVersion.Version20 },
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/3") { Version = HttpVersion.Version20 },
        };

        // Use bodyless responses with distinct status codes to identify each stream.
        // Stream IDs follow RFC 9113: client streams use odd numbers starting at 1.
        var (responses, outboundFrames) = await SendH2ManyAsync(Engine.CreateFlow(), requests, 3,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200),
            BuildH2Response(streamId: 3, status: 201),
            BuildH2Response(streamId: 5, status: 202));

        Assert.Equal(3, responses.Count);

        var statusCodes = responses.Select(r => (int)r.StatusCode).OrderBy(c => c).ToList();
        Assert.Equal([200, 201, 202], statusCodes);

        // Outbound HEADERS frames must use stream IDs 1, 3, 5 (RFC 9113 §5.1.1).
        var headerStreamIds = outboundFrames.OfType<HeadersFrame>().Select(f => f.StreamId).ToList();
        Assert.Contains(1, headerStreamIds);
        Assert.Contains(3, headerStreamIds);
        Assert.Contains(5, headerStreamIds);
    }

    // ── ST-20-007 ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.5: ST-20-007: SETTINGS frame from server is ACKed")]
    public async Task Server_Settings_Is_Acked()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200));

        var settingsAck = outboundFrames.OfType<SettingsFrame>().FirstOrDefault(f => f.IsAck);
        Assert.NotNull(settingsAck);
    }

    // ── ST-20-008 ──────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§3.4: ST-20-008: Connection preface is sent first")]
    public async Task Connection_Preface_Is_Sent_First()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        };

        var fake = new H2FakeConnectionStage(ServerSettings, BuildH2Response(streamId: 1, status: 200));
        var flow = Engine.CreateFlow().Join(
            Flow.FromGraph<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The first outbound chunk must be the connection preface.
        Assert.True(fake.OutboundChannel.Reader.TryRead(out var prefaceChunk));
        var prefaceBytes = prefaceChunk.Item1.Memory.Span[..prefaceChunk.Item2].ToArray();

        // RFC 9113 §3.4: client preface starts with this 24-byte magic string.
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        Assert.True(prefaceBytes.Length >= 24, $"Preface chunk too short: {prefaceBytes.Length} bytes");
        Assert.Equal(magic, prefaceBytes[..24]);
    }
}
