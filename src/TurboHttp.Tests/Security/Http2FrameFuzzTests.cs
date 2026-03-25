using System.Buffers.Binary;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.Security;

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
public sealed class Http2FrameFuzzTests
{
    private const int IterationsPerSeed = 100;
    private const long MaxBytesPerIteration = 1_048_576; // 1 MB

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Feeds data to the decoder and asserts the outcome is either success or
    /// <see cref="Http2Exception"/>. Any other exception is a bug.
    /// </summary>
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

    /// <summary>
    /// Builds a raw HTTP/2 frame with the given type, flags, stream ID, and payload.
    /// </summary>
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

    /// <summary>
    /// Builds a raw 9-byte frame header with the given length field, type, flags, and stream ID.
    /// The length field may not match any actual payload — this is intentional for fuzzing.
    /// </summary>
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

    /// <summary>
    /// Builds a valid SETTINGS frame on stream 0 with the given parameters.
    /// </summary>
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

    /// <summary>
    /// Builds a WINDOW_UPDATE frame with the given stream ID and raw 4-byte payload.
    /// </summary>
    private static byte[] BuildWindowUpdateFrame(int streamId, uint rawValue)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, rawValue);
        return BuildRawFrame(0x08, 0x00, streamId, payload);
    }

    /// <summary>
    /// Builds a HEADERS frame with EndHeaders set to avoid CONTINUATION state issues.
    /// </summary>
    private static byte[] BuildHeadersFrame(int streamId, byte[] headerBlock, bool endHeaders = true)
    {
        var flags = (byte)(endHeaders ? 0x04 : 0x00);
        return BuildRawFrame(0x01, flags, streamId, headerBlock);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 1: Pure random bytes (9–1024 bytes, mimicking frame header + payload)
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9113-FUZZ-RND-001: Pure random bytes never crash the frame decoder")]
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

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 2: Valid 9-byte frame header + random payload
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9113-FUZZ-HDR-001: Valid frame header with random payload handled gracefully")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleValidFrameHeaderWithRandomPayload_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            // Pick a valid frame type and appropriate stream ID
            var frameTypes = new byte[] { 0x00, 0x01, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };
            var type = frameTypes[rng.Next(frameTypes.Length)];

            // Stream 0 for SETTINGS/PING/GOAWAY, non-zero for others
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

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 3: Frame with length field > actual payload (partial frame)
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9113-FUZZ-TRN-001: Frame with length exceeding actual payload buffered without crash")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleTruncatedPayload_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            // Declare a large payload length but provide fewer bytes
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

            // Decoder should buffer incomplete frame, not crash
            var frames = decoder.Decode(data);
            Assert.Empty(frames); // Incomplete frame must not produce output

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 4: Frame with length field = 0
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9113-FUZZ-ZRO-001: Zero-length payload valid for some types, error for others")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleZeroLengthPayload_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            // Try every known frame type with zero-length payload
            for (byte type = 0x00; type <= 0x09; type++)
            {
                var streamId = type is 0x04 or 0x06 or 0x07 ? 0 : rng.Next(1, 100);
                var flags = (byte)rng.Next(0, 256);

                // For HEADERS/PUSH_PROMISE, always set EndHeaders to avoid CONTINUATION state
                if (type is 0x01 or 0x05)
                {
                    flags |= 0x04; // EndHeaders
                }

                var frame = BuildRawFrame(type, flags, streamId, Array.Empty<byte>());
                AssertDecodeNeverCrashes(decoder, frame);
                decoder.Reset(); // Reset between frame types to clear CONTINUATION state
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 5: Unknown frame type (0x0A–0xFF) — must be ignored per RFC 9113 §5.5
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9113-FUZZ-UNK-001: Unknown frame types ignored per RFC 9113 section 5.5")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_IgnoreUnknownFrameTypes_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            // Unknown frame types: 0x0A through 0xFF
            var unknownType = (byte)rng.Next(0x0A, 0x100);
            var payloadSize = rng.Next(0, 512);
            var payload = new byte[payloadSize];
            rng.NextBytes(payload);
            var streamId = rng.Next(0, 100);

            var frame = BuildRawFrame(unknownType, 0x00, streamId, payload);

            // RFC 9113 §5.5: unknown types MUST be ignored — no exception, no output
            var frames = decoder.Decode(frame);
            Assert.Empty(frames);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 6: Frame with reserved bit set — must be ignored per RFC 9113 §4.1
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9113-FUZZ-RSV-001: Reserved bit in stream ID ignored per RFC 9113 section 4.1")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_IgnoreReservedBit_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            // Build a valid DATA frame but with the reserved R bit (bit 31) set in the stream ID field
            var streamId = rng.Next(1, 100);
            var payloadSize = rng.Next(0, 256);
            var payload = new byte[payloadSize];
            rng.NextBytes(payload);

            var frame = new byte[9 + payload.Length];
            frame[0] = (byte)(payload.Length >> 16);
            frame[1] = (byte)(payload.Length >> 8);
            frame[2] = (byte)payload.Length;
            frame[3] = 0x00; // DATA
            frame[4] = 0x01; // END_STREAM
            // Set the R bit (bit 31) in the stream identifier
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId | 0x80000000u);
            payload.CopyTo(frame, 9);

            // Decoder should mask out the reserved bit and parse normally
            AssertDecodeNeverCrashes(decoder, frame);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Assert.True(allocated < MaxBytesPerIteration,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {MaxBytesPerIteration})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 7: Oversized frame (length > 16384 default max)
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9113-FUZZ-OVS-001: Oversized frames handled without OOM or crash")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleOversizedFrames_WithoutCrashing(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            // Payload larger than default SETTINGS_MAX_FRAME_SIZE (16384)
            var payloadSize = rng.Next(16385, 65536);
            var payload = new byte[payloadSize];
            rng.NextBytes(payload);

            // Try DATA and HEADERS with oversized payloads
            var frameTypes = new byte[] { 0x00, 0x01 };
            var type = frameTypes[rng.Next(frameTypes.Length)];
            var streamId = rng.Next(1, 100);
            byte flags = 0x00;
            if (type == 0x01)
            {
                flags = 0x04; // EndHeaders to avoid CONTINUATION state
            }

            var frame = BuildRawFrame(type, flags, streamId, payload);

            // The frame decoder itself does not enforce MAX_FRAME_SIZE
            // (that's a session-level concern) — it should parse without crash
            AssertDecodeNeverCrashes(decoder, frame);

            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            // Allow proportional allocation for large frames (3x payload + 1MB overhead)
            var maxAlloc = (long)payloadSize * 3 + MaxBytesPerIteration;
            Assert.True(allocated < maxAlloc,
                $"Seed {seed}, iteration {i}: allocated {allocated} bytes (limit {maxAlloc})");

            Assert.False(cts.IsCancellationRequested,
                $"Seed {seed}, iteration {i}: exceeded 5-second timeout");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 8: Rapid alternation of valid/invalid frames
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9113-FUZZ-ALT-001: Rapid valid/invalid frame alternation keeps state consistent")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_HandleRapidValidInvalidAlternation_WithConsistentState(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();
            var crashed = false;

            // Send 10–30 frames alternating between valid and invalid
            var frameCount = rng.Next(10, 31);
            for (var f = 0; f < frameCount; f++)
            {
                byte[] frame;
                if (f % 2 == 0)
                {
                    // Valid frame: SETTINGS ACK (simplest valid frame — 9 bytes, no payload)
                    frame = BuildRawFrame(0x04, 0x01, 0, Array.Empty<byte>());
                }
                else
                {
                    // Invalid frame: random bytes that form a complete frame
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
                    // Expected — reset decoder to continue testing subsequent frames
                    decoder.Reset();
                }
                catch (Exception ex)
                {
                    crashed = true;
                    Assert.Fail($"Unexpected exception on frame {f}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Assert.False(crashed);

            // Verify decoder is still usable after the sequence
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

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 9: SETTINGS frame with unknown parameters
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9113-FUZZ-SET-001: SETTINGS with unknown parameters ignored without error")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_IgnoreUnknownSettingsParameters_WithoutError(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            // Build SETTINGS frame with 1–5 unknown parameter IDs (0x07–0xFFFF)
            var paramCount = rng.Next(1, 6);
            var parameters = new (ushort id, uint value)[paramCount];
            for (var p = 0; p < paramCount; p++)
            {
                // Unknown SETTINGS parameters (IDs 0x07 and above)
                parameters[p] = ((ushort)rng.Next(0x07, 0x10000), (uint)rng.Next());
            }

            var frame = BuildSettingsFrame(parameters);

            // RFC 9113 §6.5.2: unknown SETTINGS parameters MUST be ignored
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

    // ══════════════════════════════════════════════════════════════════════════════
    // Category 10: WINDOW_UPDATE with 0 increment — PROTOCOL_ERROR
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "RFC9113-FUZZ-WUZ-001: WINDOW_UPDATE with zero increment yields PROTOCOL_ERROR")]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(65536)]
    public void Should_RejectZeroIncrementWindowUpdate_WithProtocolError(int seed)
    {
        var rng = new Random(seed);

        for (var i = 0; i < IterationsPerSeed; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var decoder = new Http2FrameDecoder();

            // WINDOW_UPDATE with zero increment on random stream
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
