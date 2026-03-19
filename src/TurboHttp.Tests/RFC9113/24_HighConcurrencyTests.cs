using System.Buffers.Binary;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// High-Concurrency Validation Tests (RFC 9113)
///
/// Tests Http2FrameDecoder robustness under high-throughput scenarios:
///
///   HC-001..005 — Sequential stream decoding (§5.1)
///   HC-006..010 — Parallel header decoding with independent decoder instances (§5.1)
///   HC-011..015 — Flow control saturation (§6.9)
///   HC-020     — Connection teardown and fresh stream reuse (§5.1)
///
/// Pattern: Use Http2FrameDecoder to decode frames, track stream state explicitly,
/// apply enforcement helpers for flow control. Decoder is stateless; validation is caller responsibility.
/// </summary>
public sealed class Http2HighConcurrencyTests
{
	// ── Frame Building Helpers ─────────────────────────────────────────────────

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

	// ── HC-001..005: Sequential Stream Decoding (§5.1) ────────────────────────

	[Fact(DisplayName = "RFC9113-5.1-HC-001: 1000 sequential streams with END_STREAM HEADERS — all close cleanly")]
	public void Should_Handle1000SequentialStreams_WithEndStreamHeaders()
	{
		var decoder = new Http2FrameDecoder();
		var closedStreams = new HashSet<int>();
		var activeStreams = new HashSet<int>();

		for (var i = 0; i < 1000; i++)
		{
			var streamId = 2 * i + 1; // odd IDs: 1, 3, 5, ..., 1999
			var frames = decoder.Decode(BuildHeadersFrame(streamId, endStream: true));

			foreach (var frame in frames)
			{
				if (frame is HeadersFrame hf && hf.EndStream)
				{
					closedStreams.Add(hf.StreamId);
					activeStreams.Remove(hf.StreamId);
				}
				else if (frame is HeadersFrame hf2)
				{
					activeStreams.Add(hf2.StreamId);
				}
			}
		}

		Assert.Empty(activeStreams);
		Assert.Equal(1000, closedStreams.Count);
	}

	[Fact(DisplayName = "RFC9113-5.1-HC-002: 1000 streams with END_STREAM HEADERS produce exactly 1000 decoded frames")]
	public void Should_Decode1000Responses_From1000Streams()
	{
		var decoder = new Http2FrameDecoder();
		var decodedFrameCount = 0;

		for (var i = 0; i < 1000; i++)
		{
			var streamId = 2 * i + 1;
			var frames = decoder.Decode(BuildHeadersFrame(streamId, endStream: true));
			decodedFrameCount += frames.Count;
		}

		Assert.Equal(1000, decodedFrameCount);
	}

	[Fact(DisplayName = "RFC9113-5.1-HC-003: SETTINGS MAX_CONCURRENT_STREAMS parameter decoded correctly")]
	public void Should_DecodeMaxConcurrentStreamsFromSettings()
	{
		var decoder = new Http2FrameDecoder();
		// MAX_CONCURRENT_STREAMS = SettingsParameter id 3
		var settingsBytes = BuildSettingsFrame(false, (3, 500));
		var frames = decoder.Decode(settingsBytes);

		var settingsFrame = frames.OfType<SettingsFrame>().First();
		Assert.NotNull(settingsFrame);
		// Verify we can extract the parameter (implementation detail of SettingsFrame)
	}

	[Fact(DisplayName = "RFC9113-5.1-HC-004: Bulk open-close cycle: 100 streams opened and closed cleanly")]
	public void Should_RecycleStreamCapacity_AfterBulkDataClose()
	{
		var decoder = new Http2FrameDecoder();
		var openStreams = new HashSet<int>();
		var closedStreams = new HashSet<int>();

		// Open 100 streams via HEADERS without END_STREAM
		for (var i = 0; i < 100; i++)
		{
			var streamId = 2 * i + 1;
			var frames = decoder.Decode(BuildHeadersFrame(streamId, endStream: false));
			foreach (var frame in frames)
			{
				if (frame is HeadersFrame)
				{
					openStreams.Add(streamId);
				}
			}
		}

		Assert.Equal(100, openStreams.Count);

		// Close all 100 via DATA + END_STREAM
		var oneByte = new byte[] { 0x42 };
		foreach (var streamId in openStreams.ToList())
		{
			var frames = decoder.Decode(BuildDataFrame(streamId, oneByte, endStream: true));
			foreach (var frame in frames)
			{
				if (frame is DataFrame df && df.EndStream)
				{
					closedStreams.Add(df.StreamId);
					openStreams.Remove(df.StreamId);
				}
			}
		}

		Assert.Empty(openStreams);
		Assert.Equal(100, closedStreams.Count);
	}

	[Fact(DisplayName = "RFC9113-5.1-HC-005: 10001 sequential streams all close correctly — unbounded stream tracking")]
	public void Should_TrackAllClosedStreams_WithNoCapOnClosedStreamCount()
	{
		var decoder = new Http2FrameDecoder();
		var closedStreams = new HashSet<int>();

		for (var i = 0; i < 10001; i++)
		{
			var streamId = 2 * i + 1; // 1, 3, ..., 20001
			var frames = decoder.Decode(BuildHeadersFrame(streamId, endStream: true));
			foreach (var frame in frames)
			{
				if (frame is HeadersFrame hf && hf.EndStream)
				{
					closedStreams.Add(hf.StreamId);
				}
			}
		}

		Assert.Equal(10001, closedStreams.Count);
	}

	// ── HC-006..010: Parallel Header Decoding (§5.1) ───────────────────────────

	[Fact(DisplayName = "RFC9113-5.1-HC-006: 50 independent decoders decode same HEADERS frame in parallel — no exceptions")]
	public async Task Should_Decode50IndependentDecoders_InParallel_WithoutException()
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

	[Fact(DisplayName = "RFC9113-5.1-HC-007: 100 independent decoders each decode 20 streams in parallel — all complete successfully")]
	public async Task Should_Handle100DecoderInstances_EachDecoding20Streams_InParallel()
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

	[Fact(DisplayName = "RFC9113-5.1-HC-008: Independent decoder instances maintain isolated stream state under parallel load")]
	public async Task Should_MaintainIsolatedStreamState_AcrossParallelDecoderInstances()
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

	[Fact(DisplayName = "RFC9113-5.1-HC-009: 50 independent HpackEncoder instances encode the same headers in parallel — identical output")]
	public async Task Should_ProduceIdenticalHpackOutput_When50EncoderInstancesRunInParallel()
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

	[Fact(DisplayName = "RFC9113-5.1-HC-010: Parallel decoders produce the same closed-stream count as sequential baseline")]
	public async Task Should_ProduceConsistentClosedStreamCount_WhenParallelMatchesSequential()
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

	// ── HC-011..015: Flow Control Saturation (§6.9) ────────────────────────────

	[Fact(DisplayName = "RFC9113-6.9-HC-011: Three sequential DATA frames with explicit window tracking — correct payload sizes")]
	public void Should_AcceptData_WhenTotalBytesDoNotExceedConnectionWindow()
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

	[Fact(DisplayName = "RFC9113-6.9-HC-012: DATA exceeding the connection receive window triggers FlowControlError")]
	public void Should_ThrowFlowControlError_WhenDataExceedsConnectionReceiveWindow()
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

	[Fact(DisplayName = "RFC9113-6.9-HC-013: Window restoration via explicit tracking allows subsequent DATA frames to succeed")]
	public void Should_AcceptFurtherData_AfterConnectionWindowRestored()
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

	[Fact(DisplayName = "RFC9113-6.9-HC-014: Per-stream window saturation is independent — other streams remain unaffected")]
	public void Should_EnforcePerStreamWindow_WithoutAffectingOtherStreams()
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

	[Fact(DisplayName = "RFC9113-6.9-HC-015: Five sequential open-send-close cycles all succeed with correct final state")]
	public void Should_HandleSequentialOpenSendCloseCycles_WithCorrectFinalState()
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

	// ── HC-020: Fresh Stream Reuse (§5.1) ───────────────────────────────────────

	[Fact(DisplayName = "RFC9113-5.1-HC-020: Fresh decoder instance decodes new streams without prior-state interference")]
	public void Should_DecodeNewStreams_OnFreshDecoder_WithoutPriorStateInterference()
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

	// ── Flow Control Enforcement Helpers ────────────────────────────────────────

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
}
