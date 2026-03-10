using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.IO;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20ConnectionPrefaceRfcTests : StreamTestBase
{
    private static readonly byte[] Http2Magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

    private static ConnectItem MakeConnect(string host = "example.com", int port = 80) =>
        new(new TcpOptions { Host = host, Port = port });

    private static DataItem MakeData(byte[] data) =>
        new(new SimpleMemoryOwner(data), data.Length);

    /// <summary>
    /// Runs the PrependPrefaceStage with the given transport items and collects all output.
    /// Returns the raw byte sequences (extracted from DataItems) and all transport items.
    /// </summary>
    private async Task<IReadOnlyList<ITransportItem>> RunAsync(params ITransportItem[] items)
    {
        return await Source.From(items)
            .Via(Flow.FromGraph(new PrependPrefaceStage()))
            .RunWith(Sink.Seq<ITransportItem>(), Materializer);
    }

    /// <summary>
    /// Extracts all raw bytes from DataItems in the output, concatenated.
    /// </summary>
    private static byte[] ExtractBytes(IReadOnlyList<ITransportItem> items)
    {
        var bytes = new List<byte>();
        foreach (var item in items)
        {
            if (item is DataItem(var owner, var length, _))
            {
                bytes.AddRange(owner.Memory.Span[..length].ToArray());
            }
        }

        return bytes.ToArray();
    }

    // ─── H2P-001: First 24 bytes = PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n ──────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§3.4-H2P-001: First 24 bytes are the HTTP/2 connection preface magic")]
    public async Task First_24_Bytes_Are_Http2_Magic()
    {
        var output = await RunAsync(
            MakeConnect(),
            MakeData(new byte[] { 0x01, 0x02 }));

        var bytes = ExtractBytes(output);

        Assert.True(bytes.Length >= 24, $"Expected at least 24 bytes, got {bytes.Length}");
        Assert.Equal(Http2Magic, bytes[..24]);
    }

    // ─── H2P-002: SETTINGS frame directly after magic (byte 24+) ────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§3.4-H2P-002: SETTINGS frame immediately follows the 24-byte magic")]
    public async Task Settings_Frame_Follows_Magic()
    {
        var output = await RunAsync(
            MakeConnect(),
            MakeData(new byte[] { 0x01, 0x02 }));

        var bytes = ExtractBytes(output);

        // After the 24-byte magic, the next 9 bytes are the SETTINGS frame header
        Assert.True(bytes.Length >= 24 + 9, $"Expected at least 33 bytes, got {bytes.Length}");

        var frameHeader = bytes.AsSpan(24, 9);

        // Frame type (byte 3) must be 0x04 = SETTINGS
        Assert.Equal((byte)FrameType.Settings, frameHeader[3]);

        // Flags (byte 4) must be 0x00 (not ACK)
        Assert.Equal(0x00, frameHeader[4]);

        // Payload length (bytes 0-2, 24-bit big-endian) must be > 0 (settings params present)
        var payloadLength = (frameHeader[0] << 16) | (frameHeader[1] << 8) | frameHeader[2];
        Assert.True(payloadLength > 0, "SETTINGS frame payload must be non-empty");

        // Verify total preface length matches: 24 (magic) + 9 (frame header) + payload
        Assert.True(bytes.Length >= 24 + 9 + payloadLength,
            $"Expected at least {24 + 9 + payloadLength} bytes for complete preface, got {bytes.Length}");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§3.4-H2P-002: SETTINGS frame contains expected default parameters")]
    public async Task Settings_Frame_Contains_Default_Parameters()
    {
        var output = await RunAsync(
            MakeConnect(),
            MakeData(new byte[] { 0x01 }));

        var bytes = ExtractBytes(output);
        var frameHeader = bytes.AsSpan(24, 9);
        var payloadLength = (frameHeader[0] << 16) | (frameHeader[1] << 8) | frameHeader[2];

        // Parse SETTINGS parameters (each is 6 bytes: 2 identifier + 4 value)
        Assert.Equal(0, payloadLength % 6);
        var paramCount = payloadLength / 6;
        Assert.True(paramCount >= 1, "At least one SETTINGS parameter expected");

        var settingsPayload = bytes.AsSpan(24 + 9, payloadLength);
        var parameters = new Dictionary<ushort, uint>();
        for (var i = 0; i < paramCount; i++)
        {
            var id = BinaryPrimitives.ReadUInt16BigEndian(settingsPayload[(i * 6)..]);
            var value = BinaryPrimitives.ReadUInt32BigEndian(settingsPayload[(i * 6 + 2)..]);
            parameters[id] = value;
        }

        // Verify known defaults from PrependPrefaceStage.BuildHttp2ConnectionPreface
        Assert.Equal(4096u, parameters[(ushort)SettingsParameter.HeaderTableSize]);
        Assert.Equal(0u, parameters[(ushort)SettingsParameter.EnablePush]);
        Assert.Equal(65535u, parameters[(ushort)SettingsParameter.InitialWindowSize]);
        Assert.Equal(16384u, parameters[(ushort)SettingsParameter.MaxFrameSize]);
    }

    // ─── H2P-003: Preface is sent exactly once (not repeated on second request) ──

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§3.4-H2P-003: Preface is sent exactly once — not repeated on subsequent data")]
    public async Task Preface_Sent_Exactly_Once()
    {
        // One connect followed by multiple data items — preface only on connect
        var output = await RunAsync(
            MakeConnect(),
            MakeData(new byte[] { 0x01 }),
            MakeData(new byte[] { 0x02 }),
            MakeData(new byte[] { 0x03 }));

        var allBytes = ExtractBytes(output);

        // Count occurrences of the HTTP/2 magic string
        var magicCount = 0;
        for (var i = 0; i <= allBytes.Length - Http2Magic.Length; i++)
        {
            if (allBytes.AsSpan(i, Http2Magic.Length).SequenceEqual(Http2Magic))
            {
                magicCount++;
            }
        }

        Assert.Equal(1, magicCount);

        // The preface DataItem is first, followed by the 3 passthrough DataItems
        var dataItems = output.OfType<DataItem>().ToList();
        Assert.Equal(4, dataItems.Count); // 1 preface + 3 data
    }

    // ─── H2P-004: SETTINGS frame on stream 0 ────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§3.4-H2P-004: SETTINGS frame in preface has stream ID 0")]
    public async Task Settings_Frame_Has_Stream_Id_Zero()
    {
        var output = await RunAsync(
            MakeConnect(),
            MakeData(new byte[] { 0x01 }));

        var bytes = ExtractBytes(output);
        Assert.True(bytes.Length >= 24 + 9, "Preface must include magic + SETTINGS frame header");

        // Stream ID is bytes 5-8 of the frame header (big-endian, top bit reserved)
        var streamIdRaw = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(24 + 5, 4));
        var streamId = (int)(streamIdRaw & 0x7FFF_FFFF); // mask reserved bit

        Assert.Equal(0, streamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§3.4-H2P-004: Reserved bit in stream ID is zero")]
    public async Task Settings_Frame_Reserved_Bit_Is_Zero()
    {
        var output = await RunAsync(
            MakeConnect(),
            MakeData(new byte[] { 0x01 }));

        var bytes = ExtractBytes(output);
        Assert.True(bytes.Length >= 24 + 9, "Preface must include magic + SETTINGS frame header");

        // Top bit of byte 5 (first byte of stream ID field) must be 0
        var firstStreamIdByte = bytes[24 + 5];
        Assert.Equal(0, firstStreamIdByte & 0x80);
    }
}
