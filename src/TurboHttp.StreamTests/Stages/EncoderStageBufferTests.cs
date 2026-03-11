using System.Buffers;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Stages;

public sealed class EncoderStageBufferTests : StreamTestBase
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<(byte[] bytes, int written)> Encode11Async(HttpRequestMessage request)
    {
        var chunks = await Source.Single(request)
            .Via(Flow.FromGraph(new Http11EncoderStage()))
            .RunWith(Sink.Seq<(IMemoryOwner<byte>, int)>(), Materializer);

        var written = 0;
        var allBytes = new List<byte>();
        foreach (var (owner, length) in chunks)
        {
            allBytes.AddRange(owner.Memory.Span[..length].ToArray());
            written += length;
            owner.Dispose();
        }

        return (allBytes.ToArray(), written);
    }

    private static int FindBodyOffset(ReadOnlySpan<byte> raw)
    {
        for (var i = 0; i <= raw.Length - 4; i++)
        {
            if (raw[i] == '\r' && raw[i + 1] == '\n' && raw[i + 2] == '\r' && raw[i + 3] == '\n')
            {
                return i + 4;
            }
        }

        return -1;
    }

    // ── BUF-001 ───────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "BUF-001: Small request (< 4 KB) → adaptive buffer starts small")]
    public async Task BUF_001_SmallRequest_WrittenBytesAreSmall()
    {
        // A bare GET with no body — encoded size is just a few dozen bytes
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path")
        {
            Version = System.Net.HttpVersion.Version11
        };

        var (_, written) = await Encode11Async(request);

        // Verify the encoded bytes fit well within the 4 KB minimum buffer
        Assert.True(written < 4 * 1024,
            $"Expected small request to encode to < 4 KB, got {written} bytes");

        // Sanity: something was actually written (at least the request line + blank line)
        Assert.True(written > 0, "Expected non-zero bytes for a valid request");
    }

    // ── BUF-002 ───────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "BUF-002: Large request (> 64 KB) → buffer grows (no overflow)")]
    public async Task BUF_002_LargeRequest_EncodesWithoutOverflow()
    {
        // 70 KB body — forces the stage to allocate a larger buffer (4 KB base + 70 KB body)
        const int bodySize = 70 * 1024;
        var bodyBytes = new byte[bodySize];
        for (var i = 0; i < bodySize; i++)
        {
            bodyBytes[i] = (byte)(i % 256);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload")
        {
            Version = System.Net.HttpVersion.Version11,
            Content = new ByteArrayContent(bodyBytes)
        };

        // Should not throw — buffer must grow to accommodate the large body
        var (raw, written) = await Encode11Async(request);

        // Total written bytes must be at least the body size
        Assert.True(written >= bodySize,
            $"Expected written ({written}) >= body size ({bodySize})");

        // Body bytes must appear verbatim after the header separator
        var bodyOffset = FindBodyOffset(raw);
        Assert.True(bodyOffset >= 0, "Expected \\r\\n\\r\\n header separator in output");

        var encodedBody = raw.AsSpan(bodyOffset);
        Assert.Equal(bodySize, encodedBody.Length);
        Assert.True(bodyBytes.AsSpan().SequenceEqual(encodedBody),
            "Body bytes in wire format must match the original body exactly");
    }

    // ── BUF-003 ───────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "BUF-003: Sequential requests → buffer reuse (no memory leak)")]
    public async Task BUF_003_SequentialRequests_AllEncodeCorrectly()
    {
        // Encode 10 sequential requests; verify each succeeds and owners are disposed.
        // The MemoryPool will reuse pooled buffers across rentals when owners are disposed,
        // demonstrating correct reuse without cross-request contamination.
        const int requestCount = 10;

        for (var i = 0; i < requestCount; i++)
        {
            var path = $"/resource/{i}";
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://example.com{path}")
            {
                Version = System.Net.HttpVersion.Version11
            };

            var chunks = await Source.Single(request)
                .Via(Flow.FromGraph(new Http11EncoderStage()))
                .RunWith(Sink.Seq<(IMemoryOwner<byte>, int)>(), Materializer);

            Assert.Single(chunks);

            var (owner, written) = chunks[0];
            try
            {
                Assert.True(written > 0, $"Request {i}: expected non-zero written bytes");

                // Verify the correct path appears in this request's encoded output
                var encoded = System.Text.Encoding.ASCII.GetString(owner.Memory.Span[..written]);
                Assert.Contains(path, encoded);
            }
            finally
            {
                // Explicit disposal returns buffer to pool — demonstrates clean reuse
                owner.Dispose();
            }
        }
    }

    // ── BUF-004 ───────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "BUF-004: Binary body → bytes passed through correctly")]
    public async Task BUF_004_BinaryBody_PassedThroughCorrectly()
    {
        // Build a 512-byte body containing the full 0x00-0xFF byte range repeated twice
        const int bodySize = 512;
        var bodyBytes = new byte[bodySize];
        for (var i = 0; i < bodySize; i++)
        {
            bodyBytes[i] = (byte)(i % 256);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/binary")
        {
            Version = System.Net.HttpVersion.Version11,
            Content = new ByteArrayContent(bodyBytes)
        };

        var (raw, written) = await Encode11Async(request);

        Assert.True(written > 0);

        // Locate the header/body boundary
        var bodyOffset = FindBodyOffset(raw);
        Assert.True(bodyOffset >= 0, "Expected \\r\\n\\r\\n separator in encoded output");

        // Verify body bytes are byte-for-byte identical to the original
        var encodedBody = raw.AsSpan(bodyOffset);
        Assert.Equal(bodySize, encodedBody.Length);
        Assert.True(bodyBytes.AsSpan().SequenceEqual(encodedBody),
            "Binary body bytes must be passed through without modification");
    }
}
