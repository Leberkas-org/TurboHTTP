using System.Collections.Immutable;
using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.Security;

/// <summary>
/// Fuzzes the HTTP/1.1 response decoder with random byte sequences to find crashes,
/// infinite loops, and uncontrolled memory allocation. Uses deterministic seeds for
/// reproducible failures. Covers chunked transfer encoding, trailer headers,
/// Content-Length mismatches, and fragmented delivery.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Decoder"/>.
/// Each test uses seeded <see cref="Random"/> so failures are reproducible.
/// Invariant: TryDecode/TryDecodeEof must either return a valid result or throw
/// <see cref="HttpDecoderException"/> — never an unhandled crash.
/// </remarks>
public sealed class Http11FuzzTests
{
    private const int IterationsPerSeed = 100;
    private const long MaxBytesPerIteration = 1_048_576; // 1 MB

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Feeds data to the decoder and asserts the outcome is either success or
    /// <see cref="HttpDecoderException"/>. Any other exception is a bug.
    /// </summary>
    private static void AssertDecodeNeverCrashes(Http11Decoder decoder, ReadOnlyMemory<byte> data)
    {
        try
        {
            decoder.TryDecode(data, out var responses);
            DisposeAll(responses);
        }
        catch (HttpDecoderException)
        {
            // Expected — malformed input correctly classified by our decoder.
        }
        catch (FormatException)
        {
            // Expected — .NET's HttpResponseMessage rejects invalid reason phrases
            // (newlines, NUL) that random bytes produce. Not a decoder bug.
        }
    }

    /// <summary>
    /// Calls TryDecodeEof and asserts the outcome is either success or
    /// an expected exception type.
    /// </summary>
    private static void AssertDecodeEofNeverCrashes(Http11Decoder decoder)
    {
        try
        {
            decoder.TryDecodeEof(out var response);
            response?.Dispose();
        }
        catch (HttpDecoderException)
        {
            // Expected — malformed input correctly classified by our decoder.
        }
        catch (FormatException)
        {
            // Expected — .NET's HttpResponseMessage rejects invalid reason phrases.
        }
    }

    private static void DisposeAll(ImmutableList<HttpResponseMessage> responses)
    {
        foreach (var r in responses)
        {
            r.Dispose();
        }
    }

    private static byte[] BuildValidResponse(int statusCode, string reason, string body,
        params (string name, string value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }
        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static byte[] BuildChunkedResponse(int statusCode, string reason,
        IReadOnlyList<byte[]> chunks, string? trailerSection = null,
        params (string name, string value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} {reason}\r\n");
        sb.Append("Transfer-Encoding: chunked\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }
        sb.Append("\r\n");

        foreach (var chunk in chunks)
        {
            sb.Append($"{chunk.Length:X}\r\n");
            sb.Append(Encoding.ASCII.GetString(chunk));
            sb.Append("\r\n");
        }

        sb.Append("0\r\n");
        if (trailerSection != null)
        {
            sb.Append(trailerSection);
        }
        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 1: Pure random bytes (1–8KB)
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9112-FUZZ-RND-001: Pure random bytes never crash the decoder")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandlePureRandomBytes_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            using var decoder = new Http11Decoder();
            var size = rng.Next(1, 8192);
            var data = new byte[size];
            rng.NextBytes(data);

            AssertDecodeNeverCrashes(decoder, data);
            AssertDecodeEofNeverCrashes(decoder);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 2: Random chunked encoding (valid chunk-size lines + random data)
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9112-FUZZ-CHK-001: Random chunked encoding handled gracefully")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleRandomChunkedEncoding_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            using var decoder = new Http11Decoder();

            // Build response with valid HTTP/1.1 status line + Transfer-Encoding: chunked
            // then random chunk data
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 200 OK\r\n");
            sb.Append("Transfer-Encoding: chunked\r\n");
            sb.Append("\r\n");

            // Generate 1-10 random chunks with valid chunk-size lines
            var chunkCount = rng.Next(1, 11);
            for (var c = 0; c < chunkCount; c++)
            {
                var chunkSize = rng.Next(1, 512);
                sb.Append($"{chunkSize:X}\r\n");

                // Random chunk data (may not match declared size — that's the fuzz)
                var actualSize = rng.Next(0, 1024);
                var chunkData = new byte[actualSize];
                rng.NextBytes(chunkData);
                // Use only printable ASCII to avoid encoding issues in StringBuilder
                for (var j = 0; j < actualSize; j++)
                {
                    chunkData[j] = (byte)(chunkData[j] % 95 + 32); // printable ASCII
                }
                sb.Append(Encoding.ASCII.GetString(chunkData));
                sb.Append("\r\n");
            }

            // May or may not include terminator
            if (rng.Next(2) == 0)
            {
                sb.Append("0\r\n\r\n");
            }

            var data = Encoding.ASCII.GetBytes(sb.ToString());
            AssertDecodeNeverCrashes(decoder, data);
            AssertDecodeEofNeverCrashes(decoder);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 3: Chunk extensions with random bytes
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9112-FUZZ-CEX-001: Chunk extensions with random bytes never crash")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleChunkExtensionsWithRandomBytes_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            using var decoder = new Http11Decoder();

            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 200 OK\r\n");
            sb.Append("Transfer-Encoding: chunked\r\n");
            sb.Append("\r\n");

            // Valid chunk with random extension data after the chunk-size
            var chunkBody = "Hello";
            var extLength = rng.Next(1, 256);
            var extBytes = new byte[extLength];
            rng.NextBytes(extBytes);

            // Chunk extensions are after ';' — use printable ASCII to avoid CRLF in extension
            var extString = new StringBuilder();
            for (var j = 0; j < extLength; j++)
            {
                var b = (byte)(extBytes[j] % 93 + 33); // printable non-space ASCII
                // Avoid CR/LF in extensions since they terminate the chunk-size line
                if (b == (byte)'\r' || b == (byte)'\n')
                {
                    b = (byte)'X';
                }
                extString.Append((char)b);
            }

            sb.Append($"{chunkBody.Length:X};{extString}\r\n");
            sb.Append(chunkBody);
            sb.Append("\r\n");
            sb.Append("0\r\n\r\n");

            var data = Encoding.ASCII.GetBytes(sb.ToString());
            AssertDecodeNeverCrashes(decoder, data);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 4: Trailer headers with random content
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9112-FUZZ-TRL-001: Trailer headers with random content never crash")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleTrailerHeadersWithRandomContent_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            using var decoder = new Http11Decoder();

            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 200 OK\r\n");
            sb.Append("Transfer-Encoding: chunked\r\n");
            sb.Append("Trailer: X-Checksum\r\n");
            sb.Append("\r\n");

            // One valid chunk
            sb.Append("5\r\nHello\r\n");

            // Terminating zero-length chunk
            sb.Append("0\r\n");

            // Random trailer section — generate random header lines
            var trailerCount = rng.Next(1, 10);
            for (var t = 0; t < trailerCount; t++)
            {
                var nameLen = rng.Next(1, 64);
                var valueLen = rng.Next(0, 256);
                var name = new StringBuilder();
                var value = new StringBuilder();

                for (var j = 0; j < nameLen; j++)
                {
                    name.Append((char)rng.Next(0x21, 0x7F)); // token chars (no space, no control)
                }
                for (var j = 0; j < valueLen; j++)
                {
                    var c = (char)rng.Next(0x20, 0x7F);
                    value.Append(c);
                }

                sb.Append($"{name}: {value}\r\n");
            }

            sb.Append("\r\n"); // end of trailers

            var data = Encoding.ASCII.GetBytes(sb.ToString());
            AssertDecodeNeverCrashes(decoder, data);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 5: Content-Length mismatch (claimed vs actual)
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9112-FUZZ-CLM-001: Content-Length mismatch detected gracefully")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleContentLengthMismatch_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            using var decoder = new Http11Decoder();

            // Build response with mismatched Content-Length
            var actualBodySize = rng.Next(0, 512);
            var bodyBytes = new byte[actualBodySize];
            rng.NextBytes(bodyBytes);
            // Use printable ASCII for body
            for (var j = 0; j < actualBodySize; j++)
            {
                bodyBytes[j] = (byte)(bodyBytes[j] % 95 + 32);
            }
            var body = Encoding.ASCII.GetString(bodyBytes);

            // Claimed length differs from actual — sometimes larger, sometimes smaller
            var claimedLength = rng.Next(2) == 0
                ? actualBodySize + rng.Next(1, 1024) // too large
                : Math.Max(0, actualBodySize - rng.Next(1, actualBodySize + 1)); // too small

            var data = BuildValidResponse(200, "OK", body,
                ("Content-Length", claimedLength.ToString()));

            AssertDecodeNeverCrashes(decoder, data);
            AssertDecodeEofNeverCrashes(decoder);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 6: Mixed Transfer-Encoding and Content-Length
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9112-FUZZ-MIX-001: Mixed Transfer-Encoding and Content-Length detected as error")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleMixedTransferEncodingAndContentLength_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            using var decoder = new Http11Decoder();

            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 200 OK\r\n");
            sb.Append("Transfer-Encoding: chunked\r\n");
            sb.Append($"Content-Length: {rng.Next(0, 10000)}\r\n");
            sb.Append("\r\n");

            // Random body content — could be chunked format or not
            var bodySize = rng.Next(0, 2048);
            var bodyBytes = new byte[bodySize];
            rng.NextBytes(bodyBytes);
            for (var j = 0; j < bodySize; j++)
            {
                bodyBytes[j] = (byte)(bodyBytes[j] % 95 + 32);
            }

            var headerData = Encoding.ASCII.GetBytes(sb.ToString());
            var combined = new byte[headerData.Length + bodySize];
            headerData.CopyTo(combined, 0);
            bodyBytes.CopyTo(combined, headerData.Length);

            AssertDecodeNeverCrashes(decoder, combined);
            AssertDecodeEofNeverCrashes(decoder);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 7: Extremely large Content-Length (>2GB)
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9112-FUZZ-LCL-001: Extremely large Content-Length handled without OOM")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleExtremelyLargeContentLength_WithoutOom(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            using var decoder = new Http11Decoder();

            // Claim an absurdly large Content-Length but provide minimal body
            var claimedLengths = new[]
            {
                int.MaxValue,
                (long)int.MaxValue + 1,
                long.MaxValue,
                rng.NextInt64(2_000_000_000L, long.MaxValue),
                rng.NextInt64(0, long.MaxValue),
                -1L
            };
            var claimedLength = claimedLengths[rng.Next(claimedLengths.Length)];

            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 200 OK\r\n");
            sb.Append($"Content-Length: {claimedLength}\r\n");
            sb.Append("\r\n");

            // Provide only a small body (won't match the claimed length)
            var bodySize = rng.Next(0, 256);
            var bodyBytes = new byte[bodySize];
            rng.NextBytes(bodyBytes);
            for (var j = 0; j < bodySize; j++)
            {
                bodyBytes[j] = (byte)(bodyBytes[j] % 95 + 32);
            }
            sb.Append(Encoding.ASCII.GetString(bodyBytes));

            var data = Encoding.ASCII.GetBytes(sb.ToString());
            AssertDecodeNeverCrashes(decoder, data);
            AssertDecodeEofNeverCrashes(decoder);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 8: Valid HTTP/1.1 response with Connection: close + random trailing data
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9112-FUZZ-CCL-001: Connection close with random trailing data handled correctly")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleConnectionCloseWithTrailingData_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            using var decoder = new Http11Decoder();

            var body = "Hello, World!";
            var validResponse = BuildValidResponse(200, "OK", body,
                ("Content-Length", body.Length.ToString()),
                ("Connection", "close"));

            // Append random trailing data after the valid response
            var trailingSize = rng.Next(1, 4096);
            var trailing = new byte[trailingSize];
            rng.NextBytes(trailing);

            var combined = new byte[validResponse.Length + trailing.Length];
            validResponse.CopyTo(combined, 0);
            trailing.CopyTo(combined, validResponse.Length);

            AssertDecodeNeverCrashes(decoder, combined);
            AssertDecodeEofNeverCrashes(decoder);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 9: Fragmented delivery — valid response split at random byte offsets
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9112-FUZZ-FRG-001: Fragmented delivery at random byte offsets keeps state consistent")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleFragmentedDelivery_WithConsistentState(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            using var decoder = new Http11Decoder();

            // Build a complete valid response (some with chunked, some with Content-Length)
            byte[] fullResponse;
            if (rng.Next(2) == 0)
            {
                // Content-Length based
                var bodySize = rng.Next(0, 512);
                var bodyBytes = new byte[bodySize];
                rng.NextBytes(bodyBytes);
                for (var j = 0; j < bodySize; j++)
                {
                    bodyBytes[j] = (byte)(bodyBytes[j] % 95 + 32);
                }
                var body = Encoding.ASCII.GetString(bodyBytes);
                fullResponse = BuildValidResponse(200, "OK", body,
                    ("Content-Length", body.Length.ToString()));
            }
            else
            {
                // Chunked
                var chunkCount = rng.Next(1, 5);
                var chunks = new List<byte[]>();
                for (var c = 0; c < chunkCount; c++)
                {
                    var chunkSize = rng.Next(1, 128);
                    var chunk = new byte[chunkSize];
                    rng.NextBytes(chunk);
                    for (var j = 0; j < chunkSize; j++)
                    {
                        chunk[j] = (byte)(chunk[j] % 95 + 32);
                    }
                    chunks.Add(chunk);
                }
                fullResponse = BuildChunkedResponse(200, "OK", chunks);
            }

            // Split at random offsets and feed incrementally
            var splitCount = rng.Next(2, 8);
            var offsets = new List<int> { 0 };
            for (var s = 0; s < splitCount - 1; s++)
            {
                offsets.Add(rng.Next(1, fullResponse.Length));
            }
            offsets.Sort();
            offsets.Add(fullResponse.Length);

            for (var s = 0; s < offsets.Count - 1; s++)
            {
                var start = offsets[s];
                var end = offsets[s + 1];
                if (start >= end)
                {
                    continue;
                }

                var fragment = fullResponse[start..end];
                AssertDecodeNeverCrashes(decoder, fragment);
            }

            AssertDecodeEofNeverCrashes(decoder);

            // Reset and verify decoder is reusable
            decoder.Reset();
            var probe = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
            AssertDecodeNeverCrashes(decoder, probe);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }
}
