using System.IO.Compression;
using System.Net;
using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http20;

/// <summary>
/// RFC 9113 — Http20Engine end-to-end round-trip tests.
/// Each test drives a full request → encoder → fake-TCP → decoder cycle
/// using <see cref="EngineTestBase.SendH2EngineAsync"/> or <see cref="EngineTestBase.SendH2EngineAsyncMany"/>.
/// </summary>
public sealed class Http20EngineRfcRoundTripTests : EngineTestBase
{
    private static Http20Engine Engine => new();

    private readonly HpackEncoder _hpack = new(useHuffman: false);

    private ReadOnlyMemory<byte> EncodeResponseHeaders(params (string Name, string Value)[] headers)
        => _hpack.Encode(headers);

    private static byte[] ServerSettings()
        => new SettingsFrame(Array.Empty<(SettingsParameter, uint)>()).Serialize();

    // ── 20ENG-001: GET → 200 — SETTINGS + HEADERS round-trip ────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-ENG-001: GET → 200 with SETTINGS and HEADERS round-trip")]
    public async Task ENG_001_Get_Returns_200_With_Settings_And_Headers_Round_Trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/hello")
        {
            Version = HttpVersion.Version20
        };

        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: EncodeResponseHeaders((":status", "200")),
            endStream: true,
            endHeaders: true).Serialize();

        var (response, outboundFrames) = await SendH2EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(outboundFrames, f => f is HeadersFrame);
    }

    // ── 20ENG-002: POST with body → outbound has HEADERS + DATA frames ───────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-ENG-002: POST with body → outbound has HEADERS + DATA frames")]
    public async Task ENG_002_Post_With_Body_Outbound_Has_Headers_And_Data_Frames()
    {
        const string payload = "field=value";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: EncodeResponseHeaders((":status", "200")),
            endStream: true,
            endHeaders: true).Serialize();

        var (response, outboundFrames) = await SendH2EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(outboundFrames, f => f is HeadersFrame);
        Assert.Contains(outboundFrames, f => f is DataFrame);
    }

    // ── 20ENG-003: gzip-compressed response → body correctly decompressed ───────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-ENG-003: gzip-compressed response body is correctly decompressed")]
    public async Task ENG_003_Gzip_Response_Body_Correctly_Decompressed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data")
        {
            Version = HttpVersion.Version20
        };

        var originalBody = "Hello, compressed HTTP/2 world!"u8.ToArray();
        byte[] compressedBody;
        using (var ms = new MemoryStream())
        {
            using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(originalBody);
            }

            compressedBody = ms.ToArray();
        }

        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: EncodeResponseHeaders(
                (":status", "200"),
                ("content-encoding", "gzip")),
            endStream: false,
            endHeaders: true).Serialize();

        var dataFrame = new DataFrame(
            streamId: 1,
            data: compressedBody,
            endStream: true).Serialize();

        var (response, _) = await SendH2EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame,
            dataFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content!.ReadAsByteArrayAsync();
        Assert.Equal(originalBody, body);
    }

    // ── 20ENG-004: Server SETTINGS → SETTINGS ACK in outbound ───────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-ENG-004: Server SETTINGS produces SETTINGS ACK in outbound frames")]
    public async Task ENG_004_Server_Settings_Produces_SettingsAck_In_Outbound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/ack-test")
        {
            Version = HttpVersion.Version20
        };

        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: EncodeResponseHeaders((":status", "200")),
            endStream: true,
            endHeaders: true).Serialize();

        var (response, outboundFrames) = await SendH2EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ack = outboundFrames.OfType<SettingsFrame>().FirstOrDefault(f => f.IsAck);
        Assert.NotNull(ack);
        Assert.Empty(ack.Parameters);
    }

    // ── 20ENG-005: 3 requests → 3 responses, stream IDs 1/3/5 ──────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-ENG-005: 3 requests produce 3 responses with outbound stream IDs 1, 3, 5")]
    public async Task ENG_005_Three_Requests_Three_Responses_Correct_Stream_Ids()
    {
        const int count = 3;
        var requests = Enumerable.Range(1, count)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, "http://example.com/item")
            {
                Version = HttpVersion.Version20
            })
            .ToList();

        // Use a single encoder so the HPACK dynamic table stays in sync
        // with the shared decoder inside Http20StreamStage.
        var enc = new HpackEncoder(useHuffman: false);
        var h1 = new HeadersFrame(streamId: 1,
            headerBlock: enc.Encode(new[] { (":status", "200") }),
            endStream: true, endHeaders: true).Serialize();
        var h3 = new HeadersFrame(streamId: 3,
            headerBlock: enc.Encode(new[] { (":status", "200") }),
            endStream: true, endHeaders: true).Serialize();
        var h5 = new HeadersFrame(streamId: 5,
            headerBlock: enc.Encode(new[] { (":status", "200") }),
            endStream: true, endHeaders: true).Serialize();

        var (responses, outboundFrames) = await SendH2EngineAsyncMany(
            Engine.CreateFlow(),
            requests,
            count,
            ServerSettings(),
            h1, h3, h5);

        Assert.Equal(count, responses.Count);

        var outboundHeaders = outboundFrames.OfType<HeadersFrame>().ToList();
        Assert.Equal(count, outboundHeaders.Count);

        // Stream IDs must be 1, 3, 5 (client-side odd IDs, ascending)
        var streamIds = outboundHeaders.Select(f => f.StreamId).OrderBy(id => id).ToList();
        Assert.Equal(new[] { 1, 3, 5 }, streamIds);
    }
}
