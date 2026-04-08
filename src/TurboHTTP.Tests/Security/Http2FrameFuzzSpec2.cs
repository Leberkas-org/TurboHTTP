using System.Buffers.Binary;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Security;

/// <summary>
/// Fuzzes the HTTP/2 frame parser with adversarial inputs targeting oversized frames,
/// rapid valid/invalid alternation, unknown SETTINGS parameters, and WINDOW_UPDATE
/// violations. Companion to <see cref="Http2FrameFuzzSpec"/> which covers categories 1–6.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// Each test uses seeded <see cref="Random"/> so failures are reproducible.
/// Invariant: Decode must either return valid frames or throw
/// <see cref="Http2Exception"/> — never an unhandled crash.
/// </remarks>
public sealed class Http2FrameFuzzSpec2
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

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Http2FrameDecoder_should_handle_oversized_frames(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            var payloadSize = rng.Next(16385, 65536);
            var payload = new byte[payloadSize];
            rng.NextBytes(payload);

            var frameTypes = new byte[] { 0x00, 0x01 };
            var type = frameTypes[rng.Next(frameTypes.Length)];
            var streamId = rng.Next(1, 100);
            byte flags = 0x00;
            if (type == 0x01)
            {
                flags = 0x04;
            }

            var frame = BuildRawFrame(type, flags, streamId, payload);

            AssertDecodeNeverCrashes(decoder, frame);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            var maxAlloc = (long)payloadSize * 3 + MaxBytesPerIteration;
            Assert.True(allocated < maxAlloc,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {maxAlloc})");

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
    public void Http2FrameDecoder_should_maintain_consistent_state_with_rapid_valid_invalid_alternation(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();
            var crashed = false;

            var frameCount = rng.Next(10, 31);
            for (var f = 0; f < frameCount; f++)
            {
                byte[] frame;
                if (f % 2 == 0)
                {
                    frame = BuildRawFrame(0x04, 0x01, 0, Array.Empty<byte>());
                }
                else
                {
                    var payloadSize = rng.Next(0, 128);
                    var payload = new byte[payloadSize];
                    rng.NextBytes(payload);
                    var type = (byte)rng.Next(0, 10);
                    var streamId = rng.Next(0, 100);
                    frame = BuildRawFrame(type, (byte)rng.Next(0, 256), streamId, payload);
                }

                try
                {
                    decoder.Decode(frame);
                }
                catch (Http2Exception)
                {
                    decoder.Reset();
                }
                catch (Exception ex)
                {
                    crashed = true;
                    Assert.Fail($"Unexpected exception on frame {f}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Assert.False(crashed);

            decoder.Reset();
            var probe = BuildRawFrame(0x04, 0x01, 0, Array.Empty<byte>());
            var probeFrames = decoder.Decode(probe);
            Assert.Single(probeFrames);

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
    public void Http2FrameDecoder_should_ignore_unknown_settings_parameters(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            var paramCount = rng.Next(1, 6);
            var parameters = new (ushort id, uint value)[paramCount];
            for (var p = 0; p < paramCount; p++)
            {
                parameters[p] = ((ushort)rng.Next(0x07, 0x10000), (uint)rng.Next());
            }

            var frame = BuildSettingsFrame(parameters);

            var frames = decoder.Decode(frame);
            Assert.Single(frames);
            Assert.IsType<SettingsFrame>(frames[0]);

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
    public void Http2FrameDecoder_should_reject_window_update_with_zero_increment(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            var streamId = rng.Next(0, 100);
            var frame = BuildWindowUpdateFrame(streamId, 0);

            var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(frame));
            Assert.Contains("0", ex.Message);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }
}
