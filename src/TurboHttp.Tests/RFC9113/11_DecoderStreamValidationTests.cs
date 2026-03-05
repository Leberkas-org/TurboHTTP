using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2DecoderStreamValidationTests
{
    [Fact(DisplayName = "7540-5.1-003: END_STREAM on incoming DATA moves stream to half-closed remote")]
    public async Task StreamState_EndStreamOnData_StreamCompleted()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, "body"u8.ToArray(), endStream: true).Serialize();

        var combined = headersFrame.Concat(dataFrame).ToArray();
        var session = new Http2ProtocolSession();
        session.Process(combined);

        // When END_STREAM arrives on DATA, the stream is half-closed remote → response produced.
        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
        var body = await session.Responses[0].Response.Content.ReadAsStringAsync();
        Assert.Equal("body", body);
    }

    [Fact(DisplayName = "7540-5.1-004: Both sides END_STREAM closes stream")]
    public void StreamState_EndStreamOnHeaders_StreamFullyClosed()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "204")]);
        // END_STREAM + END_HEADERS → stream fully closed, response produced immediately.
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        Assert.Single(session.Responses);
        Assert.Equal(204, (int)session.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-5.1-005: PUSH_PROMISE moves pushed stream to reserved remote")]
    public void StreamState_PushPromise_ReservesStream()
    {
        // Build a raw PUSH_PROMISE frame: stream=1, promised-stream=2.
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":method", "GET"), (":path", "/pushed")]);
        var ppFrame = new PushPromiseFrame(1, 2, headerBlock).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(ppFrame);

        // Promised stream ID 2 should be recorded.
        Assert.Contains(2, session.PromisedStreamIds);
    }

    [Fact(DisplayName = "7540-5.1-006: DATA on closed stream causes STREAM_CLOSED error")]
    public void StreamState_DataOnClosedStream_ThrowsStreamClosed()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        // Close stream 1 with END_STREAM on HEADERS.
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        // Now send DATA on the closed stream.
        var dataFrame = new DataFrame(1, new byte[4], endStream: false).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-5.1-007: HEADERS on closed stream is connection error STREAM_CLOSED (RFC 7540 §6.2)")]
    public void StreamState_ReuseClosedStreamId_ThrowsStreamClosed()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        // RFC 7540 §6.2: HEADERS on a closed stream is a connection error of type STREAM_CLOSED.
        var headerBlock2 = hpack.Encode([(":status", "200")]);
        var headersFrame2 = new HeadersFrame(1, headerBlock2, endStream: true, endHeaders: true).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headersFrame2));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    [Fact(DisplayName = "7540-5.1-008: Client even stream ID causes PROTOCOL_ERROR")]
    public void StreamState_EvenStreamIdWithoutPushPromise_ThrowsProtocolError()
    {
        // Build a HEADERS frame on stream 2 (even, server-push) without preceding PUSH_PROMISE.
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(2, headerBlock, endStream: true, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headersFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-5.1-009: DATA on stream 0 is PROTOCOL_ERROR")]
    public void StreamState_DataOnStream0_ThrowsProtocolError()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x04,       // length = 4
            0x00,                   // type  = DATA
            0x00,                   // flags = none
            0x00, 0x00, 0x00, 0x00, // stream ID = 0
            0x00, 0x00, 0x00, 0x00  // payload (4 bytes)
        };

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-5.1-010: HEADERS on stream 0 is PROTOCOL_ERROR")]
    public void StreamState_HeadersOnStream0_ThrowsProtocolError()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x01,       // length = 1
            0x01,                   // type  = HEADERS
            0x05,                   // flags = END_STREAM | END_HEADERS
            0x00, 0x00, 0x00, 0x00, // stream ID = 0
            0x88                    // HPACK: :status 200
        };

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }
}
