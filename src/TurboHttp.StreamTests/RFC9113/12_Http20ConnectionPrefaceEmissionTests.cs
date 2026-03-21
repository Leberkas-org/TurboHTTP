using System.Buffers.Binary;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Transport;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// RFC-tagged tests for the HTTP/2 connection preface stage per RFC 9113.
/// Verifies that the 24-byte client preface magic is emitted correctly at connection start and not repeated.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="PrependPrefaceStage"/>.
/// RFC 9113 §3.4: HTTP/2 client connection preface format and one-time emission requirement.
/// </remarks>
public sealed class Http20ConnectionPrefaceEmissionTests : StreamTestBase
{
    private static readonly byte[] Http2Magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

    private static ConnectItem MakeConnect(string host = "example.com", int port = 80) =>
        new(new TcpOptions { Host = host, Port = port })
        {
            Key = new RequestEndpoint
            {
                Host = host,
                Port = (ushort)port,
                Scheme = "http",
                Version = new Version(0, 0)
            }
        };

    private static DataItem MakeData(byte[] data) =>
        new(new SimpleMemoryOwner(data), data.Length) { Key = RequestEndpoint.Default };

    /// <summary>
    /// Runs the PrependPrefaceStage with the given transport items and collects all output.
    /// Returns the raw byte sequences (extracted from DataItems) and all transport items.
    /// </summary>
    private async Task<IReadOnlyList<IOutputItem>> RunAsync(params IOutputItem[] items)
    {
        return await Source.From(items)
            .Via(Flow.FromGraph(new PrependPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);
    }

    /// <summary>
    /// Extracts all raw bytes from DataItems in the output, concatenated.
    /// </summary>
    private static byte[] ExtractBytes(IReadOnlyList<IOutputItem> items)
    {
        var bytes = new List<byte>();
        foreach (var item in items)
        {
            if (item is DataItem(var owner, var length))
            {
                bytes.AddRange(owner.Memory.Span[..length].ToArray());
            }
        }

        return bytes.ToArray();
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-3.4-H2P-001: First 24 bytes are the HTTP/2 connection preface magic")]
    public async Task Should_Set_First_24_Bytes_As_Http2_Magic()
    {
        var output = await RunAsync(
            MakeConnect(),
            MakeData([0x01, 0x02]));

        var bytes = ExtractBytes(output);

        Assert.True(bytes.Length >= 24, $"Expected at least 24 bytes, got {bytes.Length}");
        Assert.Equal(Http2Magic, bytes[..24]);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-3.4-H2P-002: SETTINGS frame immediately follows the 24-byte magic")]
    public async Task Should_Send_Settings_Frame_After_Magic()
    {
        var output = await RunAsync(
            MakeConnect(),
            MakeData([0x01, 0x02]));

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

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-3.4-H2P-002: SETTINGS frame contains expected default parameters")]
    public async Task Should_Include_Default_Parameters_In_Settings_Frame()
    {
        var output = await RunAsync(
            MakeConnect(),
            MakeData([0x01]));

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

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-3.4-H2P-003: Preface is sent exactly once — not repeated on subsequent data")]
    public async Task Should_Send_Preface_Exactly_Once()
    {
        // One connect followed by multiple data items — preface only on connect
        var output = await RunAsync(
            MakeConnect(),
            MakeData([0x01]),
            MakeData([0x02]),
            MakeData([0x03]));

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

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-3.4-H2P-004: SETTINGS frame in preface has stream ID 0")]
    public async Task Should_Use_Stream_Id_Zero_In_Settings_Frame()
    {
        var output = await RunAsync(
            MakeConnect(),
            MakeData([0x01]));

        var bytes = ExtractBytes(output);
        Assert.True(bytes.Length >= 24 + 9, "Preface must include magic + SETTINGS frame header");

        // Stream ID is bytes 5-8 of the frame header (big-endian, top bit reserved)
        var streamIdRaw = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(24 + 5, 4));
        var streamId = (int)(streamIdRaw & 0x7FFF_FFFF); // mask reserved bit

        Assert.Equal(0, streamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-3.4-H2P-004: Reserved bit in stream ID is zero")]
    public async Task Should_Clear_Reserved_Bit_In_Settings_Frame()
    {
        var output = await RunAsync(
            MakeConnect(),
            MakeData([0x01]));

        var bytes = ExtractBytes(output);
        Assert.True(bytes.Length >= 24 + 9, "Preface must include magic + SETTINGS frame header");

        // Top bit of byte 5 (first byte of stream ID field) must be 0
        var firstStreamIdByte = bytes[24 + 5];
        Assert.Equal(0, firstStreamIdByte & 0x80);
    }
}