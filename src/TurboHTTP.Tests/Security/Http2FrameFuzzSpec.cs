using System.Buffers.Binary;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Security;

/// <summary>
/// Fuzzes the HTTP/2 frame parser with malformed and adversarial inputs to find crashes,
/// infinite loops, and uncontrolled memory allocation. Uses deterministic seeds for
/// reproducible failures.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// Each test uses seeded <see cref="Random"/> so failures are reproducible.
/// Invariant: Decode must either return valid frames or throw
/// <see cref="Http2Exception"/> — never an unhandled crash.
/// </remarks>
public sealed class Http2FrameFuzzSpec
{
    private const int IterationsPerSeed = 100;
    private const long MaxBytesPerIteration = 1_048_576;

    private static void AssertDecodeNeverCrashes(Http2FrameDecoder decoder, byte[] data)
    {
        try
        {
            decoder.Decode(data);
        }
        catch (Http2Exception)
        {
            // Expected — malformed input correctly classified by the decoder.
        }
    }

    private static byte[] BuildRawFrame(byte type, byte flags, int streamId, byte[] payload)
    {
        var frame = new byte[9 + payload.Length];
        frame[0] = (byte)(payload.Length >> 16);
        frame[1] = (byte)(payload.Length >> 8);
        frame[2] = (byte)payload.Length;
        frame[3] = type;
        frame[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId & 0x7FFFFFFFu);
        payload.CopyTo(frame, 9);
        return frame;
    }

    private static byte[] BuildRawFrameHeader(int declaredLength, byte type, byte flags, int streamId)
    {
        var header = new byte[9];
        header[0] = (byte)(declaredLength >> 16);
        header[1] = (byte)(declaredLength >> 8);
        header[2] = (byte)declaredLength;
        header[3] = type;
        header[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(5), (uint)streamId & 0x7FFFFFFFu);
        return header;
    }

    private static byte[] BuildSettingsFrame(params (ushort id, uint value)[] parameters)
    {
        var payload = new byte[parameters.Length * 6];
        for (var i = 0; i < parameters.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(i * 6), parameters[i].id);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(i * 6 + 2), parameters[i].value);
        }
        return BuildRawFrame(0x04, 0x00, 0, payload);
    }

    private static byte[] BuildWindowUpdateFrame(int streamId, uint rawValue)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, rawValue);
        return BuildRawFrame(0x08, 0x00, streamId, payload);
    }

    private static byte[] BuildHeadersFrame(int streamId, byte[] headerBlock, bool endHeaders = true)
    {
        var flags = (byte)(endHeaders ? 0x04 : 0x00);
        return BuildRawFrame(0x01, flags, streamId, headerBlock);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http2FrameDecoder_should_never_crash_when_given_pure_random_bytes(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();
            var size = rng.Next(9, 1025);
            var data = new byte[size];
            rng.NextBytes(data);

            AssertDecodeNeverCrashes(decoder, data);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http2FrameDecoder_should_handle_valid_frame_header_with_random_payload(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            var frameTypes = new byte[] { 0x00, 0x01, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };
            var type = frameTypes[rng.Next(frameTypes.Length)];

            var streamId = type is 0x04 or 0x06 or 0x07 ? 0 : rng.Next(1, 100);

            var payloadSize = rng.Next(0, 512);
            var payload = new byte[payloadSize];
            rng.NextBytes(payload);

            var flags = (byte)rng.Next(0, 256);

            var frame = BuildRawFrame(type, flags, streamId, payload);
            AssertDecodeNeverCrashes(decoder, frame);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http2FrameDecoder_should_handle_truncated_payload(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            var declaredLength = rng.Next(100, 16384);
            var actualPayloadSize = rng.Next(0, declaredLength);
            var type = (byte)rng.Next(0, 10);
            var streamId = type is 0x04 or 0x06 or 0x07 ? 0 : rng.Next(1, 100);

            var header = BuildRawFrameHeader(declaredLength, type, 0x00, streamId);
            var actualPayload = new byte[actualPayloadSize];
            rng.NextBytes(actualPayload);

            var data = new byte[header.Length + actualPayload.Length];
            header.CopyTo(data, 0);
            actualPayload.CopyTo(data, header.Length);

            var frames = decoder.Decode(data);
            Assert.Empty(frames);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http2FrameDecoder_should_handle_zero_length_payload(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            for (byte type = 0x00; type <= 0x09; type++)
            {
                var streamId = type is 0x04 or 0x06 or 0x07 ? 0 : rng.Next(1, 100);
                var flags = (byte)rng.Next(0, 256);

                if (type is 0x01 or 0x05)
                {
                    flags |= 0x04;
                }

                var frame = BuildRawFrame(type, flags, streamId, Array.Empty<byte>());
                AssertDecodeNeverCrashes(decoder, frame);
                decoder.Reset();
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http2FrameDecoder_should_ignore_unknown_frame_types(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            var unknownType = (byte)rng.Next(0x0A, 0x100);
            var payloadSize = rng.Next(0, 512);
            var payload = new byte[payloadSize];
            rng.NextBytes(payload);
            var streamId = rng.Next(0, 100);

            var frame = BuildRawFrame(unknownType, 0x00, streamId, payload);

            var frames = decoder.Decode(frame);
            Assert.Empty(frames);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http2FrameDecoder_should_ignore_reserved_bit_in_stream_id(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            var streamId = rng.Next(1, 100);
            var payloadSize = rng.Next(0, 256);
            var payload = new byte[payloadSize];
            rng.NextBytes(payload);

            var frame = new byte[9 + payload.Length];
            frame[0] = (byte)(payload.Length >> 16);
            frame[1] = (byte)(payload.Length >> 8);
            frame[2] = (byte)payload.Length;
            frame[3] = 0x00;
            frame[4] = 0x01;
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId | 0x80000000u);
            payload.CopyTo(frame, 9);

            AssertDecodeNeverCrashes(decoder, frame);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }
}
