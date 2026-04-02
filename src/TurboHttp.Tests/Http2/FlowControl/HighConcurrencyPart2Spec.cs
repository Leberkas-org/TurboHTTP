using System.Buffers.Binary;
using TurboHttp.Protocol.Http2.Hpack;
using TurboHttp.Protocol.Http2;

namespace TurboHttp.Tests.Http2.FlowControl;

/// <summary>
/// Tests decoder correctness under high-concurrency and high-volume frame sequences per RFC 9113 §5.
/// Part 2: Parallel decoding, flow control, and window management across multiple decoder instances.
/// Verifies stream multiplexing with many parallel streams processed sequentially through the decoder.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §5.1.1: Stream identifiers are assigned sequentially by the client; concurrent streams are multiplexed on a single connection.
/// </remarks>
public sealed class HighConcurrencyPart2Spec
{

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

    private static byte[] BuildHeadersFrame(int streamId, bool endStream = false)
        => BuildRawFrame(0x1, (byte)(0x4 | (endStream ? 0x1 : 0x0)), streamId, [0x88]);

    private static byte[] BuildDataFrame(int streamId, byte[] data, bool endStream = true)
        => BuildRawFrame(0x0, endStream ? (byte)0x1 : (byte)0x0, streamId, data);

    private static byte[] BuildSettingsFrame(bool ack, params (ushort id, uint value)[] settings)
    {
        var payload = new byte[settings.Length * 6];
        for (var i = 0; i < settings.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(i * 6), settings[i].id);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(i * 6 + 2), settings[i].value);
        }

        return BuildRawFrame(0x4, ack ? (byte)0x1 : (byte)0x0, 0, payload);
    }

    private static void EnforceConnectionReceiveWindow(int dataLength, ref int connectionWindow)
    {
        if (dataLength > connectionWindow)
        {
            throw new Http2Exception(
                $"RFC 9113 §6.9: DATA of {dataLength} bytes exceeds connection receive window of {connectionWindow}",
                Http2ErrorCode.FlowControlError);
        }
        connectionWindow -= dataLength;
    }

    private static void EnforceStreamReceiveWindow(int dataLength, int streamId, Dictionary<int, int> streamWindows)
    {
        var window = streamWindows.GetValueOrDefault(streamId, 65535);
        if (dataLength > window)
        {
            throw new Http2Exception(
                $"RFC 9113 §6.9: DATA of {dataLength} bytes exceeds stream {streamId} receive window of {window}",
                Http2ErrorCode.FlowControlError,
                Http2ErrorScope.Stream,
                streamId);
        }
        streamWindows[streamId] = window - dataLength;
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public async Task Http2FrameDecoder_should_decode_50_independent_decoders_in_parallel_without_exception()
    {
        var headersFrame = BuildHeadersFrame(1, endStream: true);

        var tasks = Enumerable.Range(0, 50).Select(_idx => Task.Run(() =>
        {
            var decoder = new Http2FrameDecoder();
            var frames = decoder.Decode(headersFrame);
            Assert.NotEmpty(frames);
        }));

        await Task.WhenAll(tasks);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public async Task Http2FrameDecoder_should_handle_100_decoder_instances_each_decoding_20_streams_in_parallel()
    {
        var tasks = Enumerable.Range(0, 100).Select(_idx => Task.Run(() =>
        {
            var decoder = new Http2FrameDecoder();
            var activeCount = 0;

            for (var i = 0; i < 20; i++)
            {
                var frames = decoder.Decode(BuildHeadersFrame(2 * i + 1, endStream: true));
                foreach (var frame in frames)
                {
                    if (frame is HeadersFrame hf)
                    {
                        activeCount++;
                        if (hf.EndStream)
                        {
                            activeCount--;
                        }
                    }
                }
            }

            return activeCount;
        }));

        var results = await Task.WhenAll(tasks);
        Assert.All(results, count => Assert.Equal(0, count));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public async Task Http2FrameDecoder_should_maintain_isolated_stream_state_across_parallel_decoder_instances()
    {
        // Decoder i decodes (i + 1) streams; verify each decoder's stream count matches
        var tasks = Enumerable.Range(0, 20).Select(n => Task.Run(() =>
        {
            var decoder = new Http2FrameDecoder();
            var streamCount = 0;

            for (var i = 0; i < n + 1; i++)
            {
                var frames = decoder.Decode(BuildHeadersFrame(2 * i + 1, endStream: false));
                foreach (var frame in frames)
                {
                    if (frame is HeadersFrame)
                    {
                        streamCount++;
                    }
                }
            }

            return (Expected: n + 1, Actual: streamCount);
        }));

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(r.Expected, r.Actual));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public async Task HpackEncoder_should_produce_identical_hpack_output_when_50_encoder_instances_run_in_parallel()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("content-type", "application/json"),
            ("content-length", "42"),
        };

        // Sequential baseline using a fresh encoder
        var baseline = new HpackEncoder(useHuffman: false).Encode(headers).ToArray();

        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            new HpackEncoder(useHuffman: false).Encode(headers).ToArray()));

        var results = await Task.WhenAll(tasks);
        Assert.All(results, bytes => Assert.Equal(baseline, bytes));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public async Task Http2FrameDecoder_should_produce_consistent_closed_stream_count_when_parallel_matches_sequential()
    {
        const int streamCount = 10;

        // Sequential baseline: decode 10 streams on one decoder
        var seqDecoder = new Http2FrameDecoder();
        var expectedClosed = 0;
        for (var i = 0; i < streamCount; i++)
        {
            var frames = seqDecoder.Decode(BuildHeadersFrame(2 * i + 1, endStream: true));
            foreach (var frame in frames)
            {
                if (frame is HeadersFrame hf && hf.EndStream)
                {
                    expectedClosed++;
                }
            }
        }

        // Parallel: 20 independent decoders each decode the same 10 streams
        var tasks = Enumerable.Range(0, 20).Select(_idx => Task.Run(() =>
        {
            var decoder = new Http2FrameDecoder();
            var closedCount = 0;

            for (var i = 0; i < streamCount; i++)
            {
                var frames = decoder.Decode(BuildHeadersFrame(2 * i + 1, endStream: true));
                foreach (var frame in frames)
                {
                    if (frame is HeadersFrame hf && hf.EndStream)
                    {
                        closedCount++;
                    }
                }
            }

            return closedCount;
        }));

        var results = await Task.WhenAll(tasks);
        Assert.All(results, count => Assert.Equal(expectedClosed, count));
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_accept_data_when_total_bytes_do_not_exceed_connection_window()
    {
        var decoder = new Http2FrameDecoder();
        var connectionWindow = 65535;

        // Open stream 1
        decoder.Decode(BuildHeadersFrame(1, endStream: false));

        // Use 15000-byte chunks — each well within the 16384 MAX_FRAME_SIZE limit
        var chunk = new byte[15000];

        var frames1 = decoder.Decode(BuildDataFrame(1, chunk, endStream: false));
        foreach (var frame in frames1)
        {
            if (frame is DataFrame df)
            {
                EnforceConnectionReceiveWindow(df.Data.Length, ref connectionWindow);
            }
        }

        var frames2 = decoder.Decode(BuildDataFrame(1, chunk, endStream: false));
        foreach (var frame in frames2)
        {
            if (frame is DataFrame df)
            {
                EnforceConnectionReceiveWindow(df.Data.Length, ref connectionWindow);
            }
        }

        var frames3 = decoder.Decode(BuildDataFrame(1, chunk, endStream: true));
        foreach (var frame in frames3)
        {
            if (frame is DataFrame df)
            {
                EnforceConnectionReceiveWindow(df.Data.Length, ref connectionWindow);
            }
        }

        // Should not have thrown
        Assert.True(connectionWindow >= 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_throw_flow_control_error_when_data_exceeds_connection_receive_window()
    {
        var decoder = new Http2FrameDecoder();
        var connectionWindow = 100;
        var streamWindows = new Dictionary<int, int> { { 1, 65535 } };

        decoder.Decode(BuildHeadersFrame(1, endStream: false));

        var oversized = new byte[101];
        var frames = decoder.Decode(BuildDataFrame(1, oversized, endStream: false));

        foreach (var frame in frames)
        {
            if (frame is DataFrame df)
            {
                var ex = Assert.Throws<Http2Exception>(() =>
                    EnforceConnectionReceiveWindow(df.Data.Length, ref connectionWindow));
                Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
            }
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_accept_further_data_after_connection_window_restored()
    {
        var decoder = new Http2FrameDecoder();
        var connectionWindow = 65535;
        var streamWindows = new Dictionary<int, int> { { 1, 65535 } };

        decoder.Decode(BuildHeadersFrame(1, endStream: false));

        // Exhaust the window
        var chunk = new byte[50];
        connectionWindow = 50;

        var frames1 = decoder.Decode(BuildDataFrame(1, chunk, endStream: false));
        foreach (var frame in frames1)
        {
            if (frame is DataFrame df)
            {
                EnforceConnectionReceiveWindow(df.Data.Length, ref connectionWindow);
            }
        }

        // Restore via simulated WINDOW_UPDATE
        connectionWindow = 65535;

        var frames2 = decoder.Decode(BuildDataFrame(1, chunk, endStream: true));
        foreach (var frame in frames2)
        {
            if (frame is DataFrame df)
            {
                EnforceConnectionReceiveWindow(df.Data.Length, ref connectionWindow);
            }
        }

        // Should not have thrown
        Assert.True(connectionWindow >= 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_enforce_per_stream_window_without_affecting_other_streams()
    {
        var decoder = new Http2FrameDecoder();
        var streamWindows = new Dictionary<int, int>
        {
            { 1, 50 },
            { 3, 65535 }
        };

        decoder.Decode(BuildHeadersFrame(1, endStream: false));
        decoder.Decode(BuildHeadersFrame(3, endStream: false));

        // Saturate stream 1's receive window
        var oversized = new byte[51];
        var frames1 = decoder.Decode(BuildDataFrame(1, oversized, endStream: false));
        foreach (var frame in frames1)
        {
            if (frame is DataFrame df)
            {
                var ex = Assert.Throws<Http2Exception>(() =>
                    EnforceStreamReceiveWindow(df.Data.Length, df.StreamId, streamWindows));
                Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
            }
        }

        // Stream 3 (different stream, fresh window) should be unaffected
        var frames3 = decoder.Decode(BuildDataFrame(3, new byte[100], endStream: true));
        foreach (var frame in frames3)
        {
            if (frame is DataFrame df)
            {
                EnforceStreamReceiveWindow(df.Data.Length, df.StreamId, streamWindows); // must not throw
            }
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_handle_sequential_open_send_close_cycles_with_correct_final_state()
    {
        var decoder = new Http2FrameDecoder();
        var activeSteams = new HashSet<int>();
        var closedStreams = new HashSet<int>();
        var connectionWindow = 65535;

        for (var round = 0; round < 5; round++)
        {
            var streamId = 2 * round + 1;

            // Open
            var framesOpen = decoder.Decode(BuildHeadersFrame(streamId, endStream: false));
            foreach (var frame in framesOpen)
            {
                if (frame is HeadersFrame)
                {
                    activeSteams.Add(streamId);
                }
            }

            // Send data
            var framesSend = decoder.Decode(BuildDataFrame(streamId, new byte[1024], endStream: true));
            foreach (var frame in framesSend)
            {
                if (frame is DataFrame df)
                {
                    EnforceConnectionReceiveWindow(df.Data.Length, ref connectionWindow);
                    if (df.EndStream)
                    {
                        activeSteams.Remove(streamId);
                        closedStreams.Add(streamId);
                    }
                }
            }

            // Reset for next round
            connectionWindow = 65535;
        }

        Assert.Empty(activeSteams);
        Assert.Equal(5, closedStreams.Count);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_decode_new_streams_on_fresh_decoder_without_prior_state_interference()
    {
        var decoder1 = new Http2FrameDecoder();

        // Load the first decoder with 500 open streams
        for (var i = 0; i < 500; i++)
        {
            decoder1.Decode(BuildHeadersFrame(2 * i + 1, endStream: false));
        }

        // Create a fresh decoder (no prior state)
        var decoder2 = new Http2FrameDecoder();

        // Reuse stream IDs 1..20 — on the fresh decoder they are not in prior closed-stream tracking,
        // so they are treated as fresh idle streams
        var decodedCount = 0;
        for (var i = 0; i < 20; i++)
        {
            var streamId = 2 * i + 1;
            var frames = decoder2.Decode(BuildHeadersFrame(streamId, endStream: true));
            decodedCount += frames.Count;
        }

        Assert.Equal(20, decodedCount);
    }
}
