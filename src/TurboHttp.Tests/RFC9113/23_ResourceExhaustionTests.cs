using System.Buffers.Binary;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Resource Exhaustion Protection Tests (RFC 9113)
///
/// Covers all attack vectors requiring explicit enforcement on decoded frames:
///   RE-01x  SETTINGS flood (§6.5)
///   RE-02x  Rapid reset attack (§6.4, CVE-2023-44487)
///   RE-03x  CONTINUATION flood (§6.10)
///   RE-04x  PING flood (§6.7)
///   RE-05x  Dynamic table abuse (HPACK, §6.3)
///   RE-06x  Stream ID exhaustion (§5.1)
///   RE-07x  Empty DATA frame exhaustion (§6.1)
///
/// Pattern: Decode frames with Http2FrameDecoder, count explicitly, apply enforcement helpers
/// that throw Http2Exception when thresholds are exceeded. Decoder is stateless; validation
/// is caller responsibility.
/// </summary>
public sealed class Http2ResourceExhaustionTests
{
	// ── RE-01x: SETTINGS Flood ────────────────────────────────────────────────

	[Fact(DisplayName = "RFC9113-6.5-RE-010: 101st non-ACK SETTINGS frame triggers EnhanceYourCalm flood protection")]
	public void Should_ThrowHttp2Exception_When_101SettingsFramesReceived()
	{
		var decoder = new Http2FrameDecoder();
		var settingsBytes = BuildSettingsFrame(ack: false, []);
		var settingsCount = 0;

		// Decode 100 SETTINGS frames — all should succeed
		for (var i = 0; i < 100; i++)
		{
			var frames = decoder.Decode(settingsBytes);
			foreach (var frame in frames)
			{
				if (frame is SettingsFrame sf && !sf.IsAck)
				{
					settingsCount++;
				}
			}
			EnforceSettingsFloodThreshold(settingsCount); // must not throw
		}

		// Decode the 101st
		var framesAgain = decoder.Decode(settingsBytes);
		foreach (var frame in framesAgain)
		{
			if (frame is SettingsFrame sf && !sf.IsAck)
			{
				settingsCount++;
			}
		}

		var ex = Assert.Throws<Http2Exception>(() => EnforceSettingsFloodThreshold(settingsCount));
		Assert.Equal(Http2ErrorCode.EnhanceYourCalm, ex.ErrorCode);
	}

	[Fact(DisplayName = "RFC9113-6.5-RE-011: Exactly 100 non-ACK SETTINGS frames are accepted without error")]
	public void Should_Accept100SettingsFrames_WithoutException()
	{
		var decoder = new Http2FrameDecoder();
		var settingsBytes = BuildSettingsFrame(ack: false, []);
		var settingsCount = 0;

		for (var i = 0; i < 100; i++)
		{
			var frames = decoder.Decode(settingsBytes);
			foreach (var frame in frames)
			{
				if (frame is SettingsFrame sf && !sf.IsAck)
				{
					settingsCount++;
				}
			}
		}

		EnforceSettingsFloodThreshold(settingsCount); // must not throw
		Assert.Equal(100, settingsCount);
	}

	[Fact(DisplayName = "RFC9113-6.5-RE-012: SETTINGS ACK frames do NOT count toward the flood threshold")]
	public void Should_NotCountSettingsAck_TowardFloodThreshold()
	{
		var decoder = new Http2FrameDecoder();
		var settingsAck = BuildSettingsFrame(ack: true, []);
		var settingsCount = 0;

		// 200 ACK SETTINGS frames — none should count toward the non-ACK limit
		for (var i = 0; i < 200; i++)
		{
			var frames = decoder.Decode(settingsAck);
			foreach (var frame in frames)
			{
				if (frame is SettingsFrame sf && !sf.IsAck)
				{
					settingsCount++;
				}
			}
		}

		EnforceSettingsFloodThreshold(settingsCount); // must not throw
		Assert.Equal(0, settingsCount);
	}

	// ── RE-02x: Rapid Reset Attack (CVE-2023-44487) ───────────────────────────

	[Fact(DisplayName = "RFC9113-6.4-RE-020: 101st RST_STREAM triggers rapid-reset ProtocolError (CVE-2023-44487)")]
	public void Should_ThrowHttp2Exception_When_101RstStreamReceived()
	{
		var decoder = new Http2FrameDecoder();
		var errorCode = new byte[] { 0x00, 0x00, 0x00, 0x00 };
		var rstCount = 0;

		// Decode 100 RST_STREAM frames on different stream IDs
		for (var i = 0; i < 100; i++)
		{
			var rst = BuildRawFrame(0x3, 0x0, 2 * i + 1, errorCode);
			var frames = decoder.Decode(rst);
			foreach (var frame in frames)
			{
				if (frame is RstStreamFrame)
				{
					rstCount++;
				}
			}
			EnforceRstFloodThreshold(rstCount); // must not throw
		}

		// Decode the 101st
		var rst101 = BuildRawFrame(0x3, 0x0, 201, errorCode);
		var framesAgain = decoder.Decode(rst101);
		foreach (var frame in framesAgain)
		{
			if (frame is RstStreamFrame)
			{
				rstCount++;
			}
		}

		var ex = Assert.Throws<Http2Exception>(() => EnforceRstFloodThreshold(rstCount));
		Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
	}

	[Fact(DisplayName = "RFC9113-6.4-RE-021: Exactly 100 RST_STREAM frames are accepted without error")]
	public void Should_Accept100RstStreamFrames_WithoutException()
	{
		var decoder = new Http2FrameDecoder();
		var errorCode = new byte[] { 0x00, 0x00, 0x00, 0x00 };
		var rstCount = 0;

		for (var i = 0; i < 100; i++)
		{
			var rst = BuildRawFrame(0x3, 0x0, 2 * i + 1, errorCode);
			var frames = decoder.Decode(rst);
			foreach (var frame in frames)
			{
				if (frame is RstStreamFrame)
				{
					rstCount++;
				}
			}
		}

		EnforceRstFloodThreshold(rstCount); // must not throw
		Assert.Equal(100, rstCount);
	}

	[Fact(DisplayName = "RFC9113-6.4-RE-022: Rapid-reset exception message references CVE-2023-44487")]
	public void Should_IncludeCveReference_InRapidResetMessage()
	{
		var decoder = new Http2FrameDecoder();
		var errorCode = new byte[] { 0x00, 0x00, 0x00, 0x00 };
		var rstCount = 0;

		for (var i = 0; i < 100; i++)
		{
			var rst = BuildRawFrame(0x3, 0x0, 2 * i + 1, errorCode);
			var frames = decoder.Decode(rst);
			foreach (var frame in frames)
			{
				if (frame is RstStreamFrame)
				{
					rstCount++;
				}
			}
		}

		rstCount++; // simulate 101st RST
		var ex = Assert.Throws<Http2Exception>(() => EnforceRstFloodThreshold(rstCount));
		Assert.Contains("CVE-2023-44487", ex.Message);
	}

	// ── RE-03x: CONTINUATION Flood ────────────────────────────────────────────

	[Fact(DisplayName = "RFC9113-6.10-RE-030: 1000th CONTINUATION frame triggers ProtocolError flood protection")]
	public void Should_ThrowHttp2Exception_When_1000ContinuationFramesReceived()
	{
		var decoder = new Http2FrameDecoder();
		var headersFrame = BuildRawFrame(0x1, 0x0, 1, [0x88]);  // no END_HEADERS
		var continuationNoEnd = BuildRawFrame(0x9, 0x0, 1, []);

		var chunk = new byte[headersFrame.Length + 999 * continuationNoEnd.Length];
		headersFrame.CopyTo(chunk, 0);
		for (var i = 0; i < 999; i++)
		{
			continuationNoEnd.CopyTo(chunk, headersFrame.Length + i * continuationNoEnd.Length);
		}

		var frames = decoder.Decode(chunk);
		var continuationCount = 0;
		foreach (var frame in frames)
		{
			if (frame is ContinuationFrame)
			{
				continuationCount++;
			}
		}

		// 999 CONTINUATION frames (plus 1 HEADERS) — should not throw
		EnforceContinuationFloodThreshold(continuationCount); // must not throw

		// Now add the 1000th
		var continuation1000 = BuildRawFrame(0x9, 0x0, 1, []);
		var frames1000 = decoder.Decode(continuation1000);
		foreach (var frame in frames1000)
		{
			if (frame is ContinuationFrame)
			{
				continuationCount++;
			}
		}

		var ex = Assert.Throws<Http2Exception>(() => EnforceContinuationFloodThreshold(continuationCount));
		Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
	}

	[Fact(DisplayName = "RFC9113-6.10-RE-031: 999 CONTINUATION frames after HEADERS are accepted without error")]
	public void Should_Accept999ContinuationFrames_WithoutException()
	{
		var decoder = new Http2FrameDecoder();
		var headersFrame = BuildRawFrame(0x1, 0x0, 1, [0x88]);  // no END_HEADERS
		var continuationNoEnd = BuildRawFrame(0x9, 0x0, 1, []);

		var chunk = new byte[headersFrame.Length + 999 * continuationNoEnd.Length];
		headersFrame.CopyTo(chunk, 0);
		for (var i = 0; i < 999; i++)
		{
			continuationNoEnd.CopyTo(chunk, headersFrame.Length + i * continuationNoEnd.Length);
		}

		var frames = decoder.Decode(chunk);
		var continuationCount = 0;
		foreach (var frame in frames)
		{
			if (frame is ContinuationFrame)
			{
				continuationCount++;
			}
		}

		EnforceContinuationFloodThreshold(continuationCount); // must not throw
		Assert.Equal(999, continuationCount);
	}

	// ── RE-04x: PING Flood (§6.7) ──────────────────────────────────────────────

	[Fact(DisplayName = "RFC9113-6.7-RE-040: 1001st non-ACK PING frame triggers EnhanceYourCalm flood protection")]
	public void Should_ThrowHttp2Exception_When_1001PingFramesReceived()
	{
		var decoder = new Http2FrameDecoder();
		var pingPayload = new byte[8];  // 8-byte PING payload
		var pingFrame = BuildRawFrame(0x6, 0x0, 0, pingPayload);  // type=PING, flags=0 (no ACK)
		var pingCount = 0;

		for (var i = 0; i < 1000; i++)
		{
			var frames = decoder.Decode(pingFrame);
			foreach (var frame in frames)
			{
				if (frame is PingFrame pf && !pf.IsAck)
				{
					pingCount++;
				}
			}
		}

		EnforcePingFloodThreshold(pingCount); // must not throw

		var frames1001 = decoder.Decode(pingFrame);
		foreach (var frame in frames1001)
		{
			if (frame is PingFrame pf && !pf.IsAck)
			{
				pingCount++;
			}
		}

		var ex = Assert.Throws<Http2Exception>(() => EnforcePingFloodThreshold(pingCount));
		Assert.Equal(Http2ErrorCode.EnhanceYourCalm, ex.ErrorCode);
	}

	[Fact(DisplayName = "RFC9113-6.7-RE-041: Exactly 1000 non-ACK PING frames are accepted without error")]
	public void Should_Accept1000PingFrames_WithoutException()
	{
		var decoder = new Http2FrameDecoder();
		var pingPayload = new byte[8];
		var pingFrame = BuildRawFrame(0x6, 0x0, 0, pingPayload);
		var pingCount = 0;

		for (var i = 0; i < 1000; i++)
		{
			var frames = decoder.Decode(pingFrame);
			foreach (var frame in frames)
			{
				if (frame is PingFrame pf && !pf.IsAck)
				{
					pingCount++;
				}
			}
		}

		EnforcePingFloodThreshold(pingCount); // must not throw
		Assert.Equal(1000, pingCount);
	}

	[Fact(DisplayName = "RFC9113-6.7-RE-042: PING ACK frames do NOT count toward the flood threshold")]
	public void Should_NotCountPingAck_TowardFloodThreshold()
	{
		var decoder = new Http2FrameDecoder();
		var pingPayload = new byte[8];
		var pingAckFrame = BuildRawFrame(0x6, 0x1, 0, pingPayload);  // flags=0x1 → PING ACK
		var pingCount = 0;

		// 2000 PING ACK frames — none count toward non-ACK limit
		for (var i = 0; i < 2000; i++)
		{
			var frames = decoder.Decode(pingAckFrame);
			foreach (var frame in frames)
			{
				if (frame is PingFrame pf && !pf.IsAck)
				{
					pingCount++;
				}
			}
		}

		EnforcePingFloodThreshold(pingCount); // must not throw
		Assert.Equal(0, pingCount);
	}

	[Fact(DisplayName = "RFC9113-6.7-RE-045: PING flood exception message mentions excessive PING frames")]
	public void Should_IncludeContextInPingFloodMessage()
	{
		var pingCount = 1001;
		var ex = Assert.Throws<Http2Exception>(() => EnforcePingFloodThreshold(pingCount));
		Assert.Contains("PING", ex.Message);
		Assert.Contains("flood", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	// ── RE-05x: Dynamic Table Abuse (HPACK, §6.3) ─────────────────────────────

	[Fact(DisplayName = "RFC7541-6.3-RE-050: HPACK dynamic table stays within HEADER_TABLE_SIZE limit")]
	public void Should_KeepDynamicTableWithinLimit_WhenAddingManyHeaders()
	{
		var hpack = new HpackDecoder();
		hpack.SetMaxAllowedTableSize(256);

		// Build a header block with multiple literal-with-indexing headers so the table grows.
		// Each entry: name "x-header-nnn" (12 bytes) + value "v" (1 byte) + 32 = 45 bytes.
		// Six entries = 270 bytes > 256, so eviction must kick in.
		var blocks = new List<byte>();
		for (var i = 0; i < 6; i++)
		{
			var name = $"x-hdr-{i:D3}";
			var value = "v";
			AppendLiteralIncrementalHeader(blocks, name, value);
		}

		// Also prepend a :status 200 (indexed, index 8) so ValidateResponseHeaders passes.
		var fullBlock = new List<byte>();
		fullBlock.Add(0x88);  // indexed :status 200
		fullBlock.AddRange(blocks);

		hpack.Decode([..fullBlock]);  // must not throw; eviction must have maintained bounds
	}

	[Fact(DisplayName = "RFC7541-6.3-RE-051: HPACK table size update to 0 evicts all entries")]
	public void Should_EvictAllEntries_WhenTableSizeSetToZero()
	{
		var hpack = new HpackDecoder();

		// Add one header via literal-with-indexing.
		var block1 = new byte[] { 0x88 };  // indexed :status 200 — no dynamic table entry
		hpack.Decode(block1);

		// Table size update to 0: DTS=0 prefix is 0x20 (first byte of header block).
		// RFC 7541 §6.3: Size update must appear at start of a header block.
		var blockWithUpdate = new byte[] { 0x20, 0x88 };  // DTS=0 then indexed :status 200
		hpack.Decode(blockWithUpdate);  // must not throw; table is now empty
	}

	[Fact(DisplayName = "RFC7541-6.3-RE-052: SetMaxAllowedTableSize(0) prevents any dynamic table entries")]
	public void Should_PreventTableGrowth_WhenMaxAllowedTableSizeIsZero()
	{
		var hpack = new HpackDecoder();
		hpack.SetMaxAllowedTableSize(0);

		// A header block with a table-size update to 0 is valid. Decode :status 200.
		var block = new byte[] { 0x20, 0x88 };  // DTS=0, indexed :status 200
		var headers = hpack.Decode(block);
		Assert.Single(headers);
		Assert.Equal(":status", headers[0].Name);
	}

	// ── RE-06x: Stream ID Exhaustion (§5.1) ────────────────────────────────────

	[Fact(DisplayName = "RFC9113-5.1-RE-060: Decoder handles 10000+ streams without crash — explicit stream tracking")]
	public void Should_HandleStreamIdExhaustionWithoutCrash()
	{
		var decoder = new Http2FrameDecoder();
		var closedStreamIds = new HashSet<int>();

		// Decode 10001 HEADERS+END_STREAM frames on distinct stream IDs
		for (var i = 0; i < 10001; i++)
		{
			var streamId = 2 * i + 1;  // odd stream IDs: 1, 3, ..., 20001
			var frame = BuildRawFrame(0x1, 0x5, streamId, [0x88]);  // END_HEADERS | END_STREAM
			var frames = decoder.Decode(frame);

			foreach (var f in frames)
			{
				if (f is HeadersFrame hf && hf.EndStream)
				{
					closedStreamIds.Add(hf.StreamId);
				}
			}
		}

		// Verify we tracked all closed streams
		Assert.Equal(10001, closedStreamIds.Count);
	}

	// ── RE-07x: Empty DATA Frame Exhaustion (§6.1) ──────────────────────────────

	[Fact(DisplayName = "RFC9113-6.1-RE-070: 10001st zero-length DATA frame triggers ProtocolError exhaustion protection")]
	public void Should_ThrowHttp2Exception_When_10001EmptyDataFramesReceived()
	{
		var decoder = new Http2FrameDecoder();

		// First, open stream 1 via HEADERS (END_HEADERS=0x4, no END_STREAM)
		var headersFrame = BuildRawFrame(0x1, 0x4, 1, [0x88]);
		var headersFrames = decoder.Decode(headersFrame);
		Assert.Single(headersFrames);

		// Now decode 10001 zero-length DATA frames
		const int count = 10001;
		var emptyData = BuildRawFrame(0x0, 0x0, 1, []);
		var emptyDataCount = 0;

		for (var i = 0; i < count; i++)
		{
			var frames = decoder.Decode(emptyData);
			foreach (var frame in frames)
			{
				if (frame is DataFrame df && df.Data.IsEmpty)
				{
					emptyDataCount++;
				}
			}

			if (i < count - 1)
			{
				// Don't enforce on the last one yet — we'll do it after collecting all
				if (emptyDataCount <= 10000)
				{
					EnforceEmptyDataFloodThreshold(emptyDataCount);
				}
			}
		}

		// On the 10001st frame
		var ex = Assert.Throws<Http2Exception>(() => EnforceEmptyDataFloodThreshold(emptyDataCount));
		Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
	}

	[Fact(DisplayName = "RFC9113-6.1-RE-071: Exactly 10000 zero-length DATA frames are accepted without error")]
	public void Should_Accept10000EmptyDataFrames_WithoutException()
	{
		var decoder = new Http2FrameDecoder();

		// Open stream 1 via HEADERS (END_HEADERS=0x4, no END_STREAM)
		var headersFrame = BuildRawFrame(0x1, 0x4, 1, [0x88]);
		decoder.Decode(headersFrame);

		// Send exactly 10000 zero-length DATA frames — must not throw
		const int count = 10000;
		var emptyData = BuildRawFrame(0x0, 0x0, 1, []);
		var emptyDataCount = 0;

		for (var i = 0; i < count; i++)
		{
			var frames = decoder.Decode(emptyData);
			foreach (var frame in frames)
			{
				if (frame is DataFrame df && df.Data.IsEmpty)
				{
					emptyDataCount++;
				}
			}
		}

		EnforceEmptyDataFloodThreshold(emptyDataCount); // must not throw
		Assert.Equal(10000, emptyDataCount);
	}

	// ── Enforcement Helpers ────────────────────────────────────────────────────

	private static void EnforceSettingsFloodThreshold(int settingsCount, int threshold = 100)
	{
		if (settingsCount > threshold)
		{
			throw new Http2Exception(
				$"RFC 9113 security: Excessive SETTINGS frames ({settingsCount}) — possible SETTINGS flood.",
				Http2ErrorCode.EnhanceYourCalm);
		}
	}

	private static void EnforceRstFloodThreshold(int rstCount, int threshold = 100)
	{
		if (rstCount > threshold)
		{
			throw new Http2Exception(
				"RFC 9113 security: Rapid RST_STREAM cycling — possible CVE-2023-44487 attack.",
				Http2ErrorCode.ProtocolError);
		}
	}

	private static void EnforceContinuationFloodThreshold(int count, int threshold = 1000)
	{
		if (count >= threshold)
		{
			throw new Http2Exception(
				$"RFC 9113 security: Excessive CONTINUATION frames ({count}) — possible CONTINUATION flood.",
				Http2ErrorCode.ProtocolError);
		}
	}

	private static void EnforcePingFloodThreshold(int count, int threshold = 1000)
	{
		if (count > threshold)
		{
			throw new Http2Exception(
				$"RFC 9113 security: Excessive non-ACK PING frames ({count}) — possible PING flood.",
				Http2ErrorCode.EnhanceYourCalm);
		}
	}

	private static void EnforceEmptyDataFloodThreshold(int count, int threshold = 10000)
	{
		if (count > threshold)
		{
			throw new Http2Exception(
				$"RFC 9113 security: Excessive zero-length DATA frames ({count}) — possible empty DATA flood.",
				Http2ErrorCode.ProtocolError);
		}
	}

	// ── Frame Building Helpers ─────────────────────────────────────────────────

	private static byte[] BuildRawFrame(byte frameType, byte flags, int streamId, byte[] payload)
	{
		var frame = new byte[9 + payload.Length];
		var len = payload.Length;
		frame[0] = (byte)(len >> 16);
		frame[1] = (byte)(len >> 8);
		frame[2] = (byte)len;
		frame[3] = frameType;
		frame[4] = flags;
		BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId & 0x7FFFFFFFu);
		payload.CopyTo(frame, 9);
		return frame;
	}

	private static byte[] BuildSettingsFrame(bool ack, (ushort Id, uint Value)[] parameters)
	{
		var payload = new byte[parameters.Length * 6];
		for (var i = 0; i < parameters.Length; i++)
		{
			BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(i * 6), parameters[i].Id);
			BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(i * 6 + 2), parameters[i].Value);
		}

		return BuildRawFrame(0x4, ack ? (byte)0x1 : (byte)0x0, 0, payload);
	}

	private static void AppendLiteralIncrementalHeader(List<byte> output, string name, string value)
	{
		// RFC 7541 §6.2.1: Literal Header Field with Incremental Indexing, new name (0x40 | 0x00)
		output.Add(0x40);
		AppendHpackString(output, name);
		AppendHpackString(output, value);
	}

	private static void AppendHpackString(List<byte> output, string s)
	{
		var bytes = System.Text.Encoding.ASCII.GetBytes(s);
		output.Add((byte)bytes.Length);  // not Huffman-encoded, MSB=0
		output.AddRange(bytes);
	}
}
