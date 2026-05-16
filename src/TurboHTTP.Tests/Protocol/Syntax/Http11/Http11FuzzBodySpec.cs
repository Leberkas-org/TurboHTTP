using System.Text;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11;

public sealed class Http11FuzzBodySpec
{
    private const int IterationsPerSeed = 100;
    private const long MaxBytesPerIteration = 1_048_576;
    private static readonly Http11ClientDecoderOptions DecoderOptions = Http11ClientDecoderOptions.Default;

    private static void AssertDecodeNeverCrashes(Http11ClientDecoder decoder, ReadOnlyMemory<byte> data)
    {
        try
        {
            var outcome = decoder.Feed(data.Span, requestMethodWasHead: false, out _);
            if (outcome == DecodeOutcome.Complete)
            {
                var response = decoder.GetResponse();
                response.Dispose();
                decoder.Reset();
            }
        }
        catch (HttpProtocolException)
        {
        }
        catch (FormatException)
        {
        }
    }

    private static void AssertDecodeEofNeverCrashes(Http11ClientDecoder decoder)
    {
        try
        {
            if (decoder.SignalEof() || decoder.IsBodyComplete)
            {
                var response = decoder.GetResponse();
                response?.Dispose();
                decoder.Reset();
            }
        }
        catch (HttpProtocolException)
        {
        }
        catch (FormatException)
        {
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

    [Theory(Timeout = 5000)]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http11Decoder_should_handle_mixed_transfer_encoding_and_content_length(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http11ClientDecoder(DecoderOptions);

            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 200 OK\r\n");
            sb.Append("Transfer-Encoding: chunked\r\n");
            sb.Append($"Content-Length: {rng.Next(0, 10000)}\r\n");
            sb.Append("\r\n");

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

    [Theory(Timeout = 5000)]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http11Decoder_should_handle_extremely_large_content_length_without_oom(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http11ClientDecoder(DecoderOptions);

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

    [Theory(Timeout = 5000)]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http11Decoder_should_handle_connection_close_with_trailing_data(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http11ClientDecoder(DecoderOptions);

            var body = "Hello, World!";
            var validResponse = BuildValidResponse(200, "OK", body,
                ("Content-Length", body.Length.ToString()),
                ("Connection", "close"));

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

    [Theory(Timeout = 5000)]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http11Decoder_should_maintain_consistent_state_with_fragmented_delivery(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http11ClientDecoder(DecoderOptions);

            byte[] fullResponse;
            if (rng.Next(2) == 0)
            {
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

            decoder.Reset();
            var probe = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
            AssertDecodeNeverCrashes(decoder, probe);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }
}