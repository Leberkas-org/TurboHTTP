using System.Buffers;
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http2WireComplianceTests : EngineTestBase
{
    private static Http20Engine Engine => new();

    private static readonly byte[] ServerSettings = new SettingsFrame([]).Serialize();

    private static byte[] BuildH2Response(int streamId, int status)
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", status.ToString())]);
        return new HeadersFrame(streamId, headerBlock, endStream: true, endHeaders: true).Serialize();
    }

    /// <summary>
    /// Runs the engine through a round-trip and returns the raw bytes of the very first
    /// outbound chunk (the connection preface: 24-byte magic + client SETTINGS frame).
    /// </summary>
    private async Task<byte[]> GetFirstOutboundChunkAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        };

        var fake = new H2FakeConnectionStage(ServerSettings, BuildH2Response(1, 200));
        var flow = Engine.CreateFlow().Join(
            Flow.FromGraph<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(fake.OutboundChannel.Reader.TryRead(out var firstChunk));
        return firstChunk.Item1.Memory.Span[..firstChunk.Item2].ToArray();
    }

    /// <summary>
    /// Runs the engine through a round-trip and collects all outbound raw bytes
    /// (including the preface chunk) as a single contiguous array.
    /// </summary>
    private async Task<byte[]> GetAllOutboundBytesAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path")
        {
            Version = HttpVersion.Version20
        };

        var fake = new H2FakeConnectionStage(ServerSettings, BuildH2Response(1, 200));
        var flow = Engine.CreateFlow().Join(
            Flow.FromGraph<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var allBytes = new List<byte>();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            allBytes.AddRange(chunk.Item1.Memory.Span[..chunk.Item2].ToArray());
        }

        return allBytes.ToArray();
    }

    // ── ST-20-WIRE-001 ─────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§3.5: ST-20-WIRE-001: First 24 bytes equal connection preface magic verbatim")]
    public async Task ST_20_WIRE_001_Connection_Preface_Magic_First24Bytes()
    {
        var preface = await GetFirstOutboundChunkAsync();

        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        Assert.True(preface.Length >= 24, $"Preface chunk too short: {preface.Length} bytes");
        Assert.Equal(magic, preface[..24]);
    }

    // ── ST-20-WIRE-002 ─────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§3.5: ST-20-WIRE-002: SETTINGS frame immediately follows preface at byte offset 24")]
    public async Task ST_20_WIRE_002_Settings_Frame_Follows_Preface_At_Offset_24()
    {
        var preface = await GetFirstOutboundChunkAsync();

        // Frame header at offset 24 (immediately after 24-byte magic):
        //   [24..26] = length (3 bytes)
        //   [27]     = type
        //   [28]     = flags
        //   [29..32] = stream-id
        Assert.True(preface.Length >= 33, $"Expected at least 33 bytes, got {preface.Length}");
        Assert.Equal(0x04, preface[27]); // type = SETTINGS
        Assert.Equal(0x00, preface[28]); // flags = 0 (not an ACK)
        // Stream ID must be 0 for connection-level frames.
        Assert.Equal(0x00, preface[29]);
        Assert.Equal(0x00, preface[30]);
        Assert.Equal(0x00, preface[31]);
        Assert.Equal(0x00, preface[32]);
    }

    // ── ST-20-WIRE-003 ─────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1: ST-20-WIRE-003: HPACK block contains :method :path :scheme :authority")]
    public async Task ST_20_WIRE_003_Hpack_PseudoHeaders_All_Present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200));

        var headersFrame = outboundFrames.OfType<HeadersFrame>().FirstOrDefault();
        Assert.NotNull(headersFrame);

        var hpack = new HpackDecoder();
        var decoded = hpack.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.Contains(decoded, h => h.Name == ":method");
        Assert.Contains(decoded, h => h.Name == ":path");
        Assert.Contains(decoded, h => h.Name == ":scheme");
        Assert.Contains(decoded, h => h.Name == ":authority");
    }

    // ── ST-20-WIRE-004 ─────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§4.1: ST-20-WIRE-004: All frame length fields consistent with actual payload sizes")]
    public async Task ST_20_WIRE_004_All_Frame_Length_Fields_Consistent()
    {
        var raw = await GetAllOutboundBytesAsync();

        // The first 24 bytes are the preface magic string (not a frame).
        const int prefaceMagicLength = 24;
        Assert.True(raw.Length >= prefaceMagicLength, "Outbound bytes shorter than preface magic");

        var pos = prefaceMagicLength;
        var frameCount = 0;

        while (pos < raw.Length)
        {
            Assert.True(pos + 9 <= raw.Length,
                $"Incomplete frame header at offset {pos} (only {raw.Length - pos} bytes remain)");

            // Length is a 24-bit big-endian field at the start of each frame header.
            var declaredLength = (raw[pos] << 16) | (raw[pos + 1] << 8) | raw[pos + 2];

            Assert.True(pos + 9 + declaredLength <= raw.Length,
                $"Frame at offset {pos} declares payload length {declaredLength} " +
                $"but only {raw.Length - pos - 9} bytes remain after header");

            pos += 9 + declaredLength;
            frameCount++;
        }

        Assert.True(frameCount >= 1, "Expected at least one frame after the preface magic");
        Assert.Equal(raw.Length, pos); // All bytes consumed with exact frame boundaries
    }

    // ── ST-20-WIRE-005 ─────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§5.1.1: ST-20-WIRE-005: First request stream ID is 1")]
    public async Task ST_20_WIRE_005_First_Request_StreamId_Is_1()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200));

        var firstHeaders = outboundFrames.OfType<HeadersFrame>().FirstOrDefault();
        Assert.NotNull(firstHeaders);
        Assert.Equal(1, firstHeaders.StreamId);
    }

    // ── ST-20-WIRE-006 ─────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§6.5: ST-20-WIRE-006: SETTINGS ACK flags byte is 0x01")]
    public async Task ST_20_WIRE_006_Settings_Ack_Flags_Byte_Is_0x01()
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

        // Verify the serialised ACK frame has the flags byte (offset 4) set to 0x01.
        var ackBytes = settingsAck.Serialize();
        Assert.Equal(0x01, ackBytes[4]);
    }
}
