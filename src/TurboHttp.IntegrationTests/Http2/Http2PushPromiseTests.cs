using System.Buffers.Binary;
using System.Net;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http2;

/// <summary>
/// Phase 16 — HTTP/2 Advanced: PUSH_PROMISE tests.
/// Kestrel does not send PUSH_PROMISE by default (server push is deprecated in RFC 9113),
/// so these tests validate the decoder's PUSH_PROMISE handling using raw frame construction.
/// </summary>
[Collection("Http2Advanced")]
public sealed class Http2PushPromiseTests
{
    private readonly KestrelH2Fixture _fixture;

    public Http2PushPromiseTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
    }

    // ── PUSH_PROMISE Decoder Tests ────────────────────────────────────────────

    [Fact(DisplayName = "IT-2A-020: PUSH_PROMISE received and decoded — promised stream ID returned")]
    public void Should_ReturnPromisedStreamId_When_PushPromiseDecoded()
    {
        var decoder = new Http2Decoder();
        var pushPromise = BuildPushPromiseFrame(parentStreamId: 1, promisedStreamId: 2);

        var decoded = decoder.TryDecode(pushPromise.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Contains(2, result.PromisedStreamIds);
    }

    [Fact(DisplayName = "IT-2A-021: Push stream ID is even (server-initiated) — decoder accepts it")]
    public void Should_AcceptEvenStreamId_When_AnnouncedViaPushPromise()
    {
        var decoder = new Http2Decoder();
        // Even stream IDs are server-initiated; they must be pre-announced via PUSH_PROMISE.
        var pushPromise = BuildPushPromiseFrame(parentStreamId: 1, promisedStreamId: 4);

        decoder.TryDecode(pushPromise.AsMemory(), out var result);

        Assert.Contains(4, result.PromisedStreamIds);

        // Now HEADERS on stream 4 must NOT throw — it was promised.
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]);
        var headersFrame = new byte[9 + headerBlock.Length];
        Http2FrameWriter.WriteHeadersFrame(headersFrame, streamId: 4, headerBlock.Span, endStream: true, endHeaders: true);

        var decoded = decoder.TryDecode(headersFrame.AsMemory(), out var result2);
        Assert.True(decoded);
        Assert.Single(result2.Responses);
        Assert.Equal(4, result2.Responses[0].StreamId);
    }

    [Fact(DisplayName = "IT-2A-022: PUSH_PROMISE header block present — decoder processes frame without error")]
    public void Should_ProcessWithoutError_When_PushPromiseContainsHeaderBlock()
    {
        var decoder = new Http2Decoder();
        // Build PUSH_PROMISE with a full HPACK header block (request headers for the push).
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([
            (":method", "GET"),
            (":path", "/pushed-resource"),
            (":scheme", "http"),
            (":authority", "127.0.0.1"),
        ]);

        var pushPromise = BuildPushPromiseFrame(parentStreamId: 1, promisedStreamId: 2, headerBlock: headerBlock.Span);

        var decoded = decoder.TryDecode(pushPromise.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Contains(2, result.PromisedStreamIds);
        Assert.Empty(result.Responses); // No response yet — push response comes separately
    }

    [Fact(DisplayName = "IT-2A-023: Push stream DATA frames received — response assembled correctly")]
    public async Task Should_AssemblePushResponse_When_HeadersThenDataOnPushStream()
    {
        var decoder = new Http2Decoder();

        // Step 1: PUSH_PROMISE on stream 1 promising stream 2
        var pushPromise = BuildPushPromiseFrame(parentStreamId: 1, promisedStreamId: 2);
        decoder.TryDecode(pushPromise.AsMemory(), out _);

        // Step 2: HEADERS on stream 2 (the push response headers, no END_STREAM)
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]);
        var headersFrame = new byte[9 + headerBlock.Length];
        Http2FrameWriter.WriteHeadersFrame(headersFrame, streamId: 2, headerBlock.Span, endStream: false, endHeaders: true);
        decoder.TryDecode(headersFrame.AsMemory(), out _);

        // Step 3: DATA on stream 2 with END_STREAM
        var dataPayload = "pushed-content"u8.ToArray();
        var dataFrame = new byte[9 + dataPayload.Length];
        Http2FrameWriter.WriteDataFrame(dataFrame, streamId: 2, dataPayload, endStream: true);
        var decoded = decoder.TryDecode(dataFrame.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Single(result.Responses);
        Assert.Equal(2, result.Responses[0].StreamId);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
        var body = result.Responses[0].Response.Content is not null
            ? await result.Responses[0].Response.Content.ReadAsByteArrayAsync()
            : null;
        Assert.Equal(dataPayload, body);
    }

    [Fact(DisplayName = "IT-2A-024: RST_STREAM on pushed stream (refuse push) — stream closed as cancelled")]
    public void Should_CloseStream_When_RstStreamSentOnPushedStream()
    {
        var decoder = new Http2Decoder();

        // Announce push on stream 2
        var pushPromise = BuildPushPromiseFrame(parentStreamId: 1, promisedStreamId: 2);
        decoder.TryDecode(pushPromise.AsMemory(), out _);

        // Client refuses push by sending RST_STREAM(2, CANCEL)
        var rst = Http2Encoder.EncodeRstStream(2, Http2ErrorCode.Cancel);
        decoder.TryDecode(rst.AsMemory(), out var result);

        Assert.Single(result.RstStreams);
        Assert.Equal(2, result.RstStreams[0].StreamId);
        Assert.Equal(Http2ErrorCode.Cancel, result.RstStreams[0].Error);
    }

    [Fact(DisplayName = "IT-2A-025: PUSH_PROMISE disabled via SETTINGS_ENABLE_PUSH=0 in client preface")]
    public void Should_IncludeEnablePushZero_When_ClientPrefaceBuilt()
    {
        // The encoder's connection preface always includes SETTINGS_ENABLE_PUSH=0.
        var preface = Http2Encoder.BuildConnectionPreface();

        // Skip the 24-byte magic string and find the SETTINGS frame payload.
        var settingsPayload = preface.AsSpan(24 + 9); // skip magic + frame header
        var foundEnablePushZero = false;
        for (var i = 0; i + 6 <= settingsPayload.Length; i += 6)
        {
            var param = (SettingsParameter)BinaryPrimitives.ReadUInt16BigEndian(settingsPayload[i..]);
            var value = BinaryPrimitives.ReadUInt32BigEndian(settingsPayload[(i + 2)..]);
            if (param == SettingsParameter.EnablePush && value == 0)
            {
                foundEnablePushZero = true;
                break;
            }
        }

        Assert.True(foundEnablePushZero,
            "Client connection preface SETTINGS must contain SETTINGS_ENABLE_PUSH = 0.");
    }

    [Fact(DisplayName = "IT-2A-026: PUSH_PROMISE with :path and :status-equivalent pseudo-headers decoded")]
    public void Should_DecodeHeaderBlock_When_PushPromiseContainsRequestPseudoHeaders()
    {
        var decoder = new Http2Decoder();
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var requestHeaders = hpackEncoder.Encode([
            (":method", "GET"),
            (":path", "/api/resource"),
            (":scheme", "https"),
            (":authority", "example.com"),
        ]);

        var frame = BuildPushPromiseFrame(parentStreamId: 1, promisedStreamId: 6, headerBlock: requestHeaders.Span);
        var decoded = decoder.TryDecode(frame.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Contains(6, result.PromisedStreamIds);
    }

    [Fact(DisplayName = "IT-2A-027: Multiple push promises in one response — all stream IDs registered")]
    public void Should_RegisterAllPromisedStreams_When_MultiplePushPromisesReceived()
    {
        var decoder = new Http2Decoder();

        var pp1 = BuildPushPromiseFrame(parentStreamId: 1, promisedStreamId: 2);
        var pp2 = BuildPushPromiseFrame(parentStreamId: 1, promisedStreamId: 4);
        var pp3 = BuildPushPromiseFrame(parentStreamId: 1, promisedStreamId: 6);

        // Feed all three frames as a single batch.
        var batch = new byte[pp1.Length + pp2.Length + pp3.Length];
        pp1.CopyTo(batch, 0);
        pp2.CopyTo(batch, pp1.Length);
        pp3.CopyTo(batch, pp1.Length + pp2.Length);

        decoder.TryDecode(batch.AsMemory(), out var result);

        Assert.Contains(2, result.PromisedStreamIds);
        Assert.Contains(4, result.PromisedStreamIds);
        Assert.Contains(6, result.PromisedStreamIds);
    }

    [Fact(DisplayName = "IT-2A-028: Push promise on stream 1 → push stream 2 — decoder tracks mapping")]
    public void Should_TrackPushStreamId_When_PushPromiseReceivedOnStream1()
    {
        var decoder = new Http2Decoder();
        var pushPromise = BuildPushPromiseFrame(parentStreamId: 1, promisedStreamId: 2);

        decoder.TryDecode(pushPromise.AsMemory(), out var result);

        Assert.Single(result.PromisedStreamIds);
        Assert.Equal(2, result.PromisedStreamIds[0]);
    }

    [Fact(DisplayName = "IT-2A-029: Push stream END_STREAM on HEADERS — response returned immediately")]
    public void Should_ReturnResponse_When_PushStreamHasEndStreamOnHeaders()
    {
        var decoder = new Http2Decoder();

        // Register push stream 2 via PUSH_PROMISE
        var pushPromise = BuildPushPromiseFrame(parentStreamId: 1, promisedStreamId: 2);
        decoder.TryDecode(pushPromise.AsMemory(), out _);

        // HEADERS on stream 2 with END_STREAM + END_HEADERS (header-only push response)
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]);
        var headersFrame = new byte[9 + headerBlock.Length];
        Http2FrameWriter.WriteHeadersFrame(headersFrame, streamId: 2, headerBlock.Span, endStream: true, endHeaders: true);

        var decoded = decoder.TryDecode(headersFrame.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.Single(result.Responses);
        Assert.Equal(2, result.Responses[0].StreamId);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a PUSH_PROMISE frame with the given parent stream, promised stream ID,
    /// and optional HPACK header block.
    /// </summary>
    private static byte[] BuildPushPromiseFrame(
        int parentStreamId,
        int promisedStreamId,
        ReadOnlySpan<byte> headerBlock = default)
    {
        // PUSH_PROMISE payload: 4 bytes promised stream ID + header block
        var payloadLength = 4 + headerBlock.Length;
        var frame = new byte[9 + payloadLength];

        // Frame header: length, type=PUSH_PROMISE(0x5), flags=END_HEADERS(0x4), parentStreamId
        Http2FrameWriter.WriteFrameHeader(frame, payloadLength, FrameType.PushPromise, 0x4, parentStreamId);

        // Promised stream ID (R-bit must be 0)
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(9), (uint)promisedStreamId & 0x7FFFFFFFu);

        // Header block (if any)
        if (!headerBlock.IsEmpty)
        {
            headerBlock.CopyTo(frame.AsSpan(13));
        }

        return frame;
    }
}
