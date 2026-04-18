using System.Text;
using TurboHTTP.Protocol;
using Decoder = TurboHTTP.Protocol.Http10.Decoder;

namespace TurboHTTP.Tests.Security;

public sealed class Http10FuzzSpec
{
    private const int IterationsPerSeed = 100;
    private const long MaxBytesPerIteration = 1_048_576;

    private static void AssertDecodeNeverCrashes(Decoder decoder, ReadOnlyMemory<byte> data)
    {
        try
        {
            decoder.TryDecode(data, out var response);
            response?.Dispose();
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

    private static void AssertDecodeEofNeverCrashes(Decoder decoder)
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

    private static byte[] BuildValidStatusLine(int statusCode = 200, string reason = "OK")
    {
        return Encoding.ASCII.GetBytes($"HTTP/1.0 {statusCode} {reason}\r\n");
    }

    private static byte[] BuildValidResponse(int statusCode, string reason, string body,
        params (string name, string value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.0 {statusCode} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }
        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Theory(Timeout = 5000)]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http10Decoder_should_never_crash_when_given_pure_random_bytes(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Decoder();
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

    [Theory(Timeout = 5000)]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http10Decoder_should_handle_partial_valid_responses(int seed)
    {
        var rng = new Random(seed);
        var statusLine = BuildValidStatusLine();

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Decoder();

            var randomPart = new byte[rng.Next(0, 4096)];
            rng.NextBytes(randomPart);

            var combined = new byte[statusLine.Length + randomPart.Length];
            statusLine.CopyTo(combined, 0);
            randomPart.CopyTo(combined, statusLine.Length);

            AssertDecodeNeverCrashes(decoder, combined);
            AssertDecodeEofNeverCrashes(decoder);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    [Theory(Timeout = 5000)]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http10Decoder_should_handle_truncated_responses(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var bodySize = rng.Next(0, 512);
            var bodyBytes = new byte[bodySize];
            rng.NextBytes(bodyBytes);
            var body = Convert.ToBase64String(bodyBytes);

            var fullResponse = BuildValidResponse(200, "OK", body,
                ("Content-Length", body.Length.ToString()));

            var truncateAt = rng.Next(1, fullResponse.Length);
            var truncated = fullResponse[..truncateAt];

            var decoder = new Decoder();
            AssertDecodeNeverCrashes(decoder, truncated);
            AssertDecodeEofNeverCrashes(decoder);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    [Theory(Timeout = 5000)]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http10Decoder_should_handle_oversized_headers_with_bounded_memory(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var decoder = new Decoder();

            var headerValueSize = rng.Next(65536, 131072);
            var sb = new StringBuilder("HTTP/1.0 200 OK\r\n");
            sb.Append("X-Large: ");

            for (var j = 0; j < headerValueSize; j++)
            {
                sb.Append((char)rng.Next(0x20, 0x7F));
            }

            sb.Append("\r\n\r\n");
            var data = Encoding.ASCII.GetBytes(sb.ToString());

            var maxAlloc = (long)data.Length * 3 + MaxBytesPerIteration;
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            AssertDecodeNeverCrashes(decoder, data);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < maxAlloc,
                $"Seed {seed}, iteration {i}: decoder allocated {allocated} bytes (limit {maxAlloc})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    [Theory(Timeout = 5000)]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http10Decoder_should_handle_valid_response_followed_by_garbage(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Decoder();

            var body = "Hello, World!";
            var validResponse = BuildValidResponse(200, "OK", body,
                ("Content-Length", body.Length.ToString()));

            var garbageSize = rng.Next(1, 4096);
            var garbage = new byte[garbageSize];
            rng.NextBytes(garbage);

            var combined = new byte[validResponse.Length + garbage.Length];
            validResponse.CopyTo(combined, 0);
            garbage.CopyTo(combined, validResponse.Length);

            AssertDecodeNeverCrashes(decoder, combined);
            AssertDecodeEofNeverCrashes(decoder);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    [Theory(Timeout = 5000)]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http10Decoder_should_maintain_consistent_state_with_incremental_random_chunks(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Decoder();

            var chunkCount = rng.Next(5, 21);
            for (var c = 0; c < chunkCount; c++)
            {
                var chunkSize = rng.Next(1, 512);
                var chunk = new byte[chunkSize];
                rng.NextBytes(chunk);

                AssertDecodeNeverCrashes(decoder, chunk);
            }

            AssertDecodeEofNeverCrashes(decoder);

            decoder.Reset();
            var probe = "HTTP/1.0 200 OK\r\n\r\n"u8.ToArray();
            AssertDecodeNeverCrashes(decoder, probe);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }
}
