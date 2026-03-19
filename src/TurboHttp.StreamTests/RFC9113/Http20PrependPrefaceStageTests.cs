using System.Buffers;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// Tests the HTTP/2 connection preface injection stage per RFC 9113.
/// Verifies that the client connection preface magic bytes are prepended exactly once at the start of a connection.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="PrependPrefaceStage"/>.
/// RFC 9113 §3.4: HTTP/2 client connection preface requirement and format.
/// </remarks>
public sealed class Http20PrependPrefaceStageTests : StreamTestBase
{
    private static readonly byte[] PrefaceMagic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

    private static readonly ConnectItem DefaultConnect =
        new(new TcpOptions { Host = "test.local", Port = 443 })
        {
            Key = new RequestEndpoint
            {
                Host = "test.local",
                Port = 443,
                Scheme = "http",
                Version = new Version(0, 0)
            }
        };

    // Prepends a ConnectItem (triggers preface), then wraps byte[] inputs as DataItems.
    // Returns only DataItem outputs, copied out as byte[].
    private async Task<IReadOnlyList<byte[]>> RunAsync(params byte[][] inputs)
    {
        var items = new List<IOutputItem> { DefaultConnect };
        items.AddRange(inputs.Select(IOutputItem (b) =>
        {
            IMemoryOwner<byte> owner = new SimpleMemoryOwner(b);
            return new DataItem(owner, b.Length) { Key = RequestEndpoint.Default };
        }));

        var source = Source.From(items);

        var chunks = await source
            .Via(Flow.FromGraph(new PrependPrefaceStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        return chunks.OfType<DataItem>().Select(c =>
        {
            var bytes = c.Memory.Memory.Span[..c.Length].ToArray();
            c.Memory.Dispose();
            return bytes;
        }).ToList();
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-3.5: First 24 bytes are exactly the connection preface magic")]
    public async Task Should_Emit_Magic_As_First_24_Bytes()
    {
        var outputs = await RunAsync([0x01]);

        Assert.True(outputs.Count >= 1);
        var first = outputs[0];
        Assert.True(first.Length >= 24, $"Preface chunk must be at least 24 bytes, got {first.Length}");
        Assert.Equal(PrefaceMagic, first[..24]);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-3.5: Bytes 24..32 are a SETTINGS frame header (type=0x4, stream=0)")]
    public async Task Should_Emit_Settings_Frame_Header_After_Magic()
    {
        var outputs = await RunAsync([0x01]);

        var first = outputs[0];
        // SETTINGS frame header starts at byte 24 (immediately after the 24-byte magic).
        // Layout: [24..26]=length, [27]=type, [28]=flags, [29..32]=stream-id
        Assert.True(first.Length >= 33, $"Expected at least 33 bytes, got {first.Length}");
        Assert.Equal(0x04, first[27]); // frame type = SETTINGS
        Assert.Equal(0x00, first[28]); // flags = 0 (not an ACK)
        Assert.Equal(0x00, first[29]); // stream ID high byte
        Assert.Equal(0x00, first[30]);
        Assert.Equal(0x00, first[31]);
        Assert.Equal(0x00, first[32]); // stream ID = 0 (connection-level)
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-3.5: Second element passed through unchanged after preface emitted")]
    public async Task Should_Pass_Through_Items_After_Preface()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var outputs = await RunAsync(payload);

        Assert.Equal(2, outputs.Count);
        Assert.Equal(payload, outputs[1]);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "RFC9113-3.5: Preface emitted exactly once (not repeated for second demand)")]
    public async Task Should_Emit_Preface_Exactly_Once()
    {
        var outputs = await RunAsync([0x01], [0x02], [0x03]);

        // Preface + 3 passthrough chunks = 4 total
        Assert.Equal(4, outputs.Count);

        var prefaceCount =
            outputs.Count(chunk => chunk.Length >= 24 && chunk.AsSpan()[..24].SequenceEqual(PrefaceMagic));

        Assert.Equal(1, prefaceCount);
    }
}