using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Phase 12-13: HTTP/2 Decoder — HEADERS Validation.
/// RFC 9113 §8.2 (header name rules), §8.2.2 (connection-specific forbidden),
/// §8.3 (pseudo-header ordering), §8.3.2 (response pseudo-header requirements).
/// </summary>
public sealed class Http2DecoderHeadersValidationTests
{
    // ── Helper: build a raw HEADERS frame with a literal-encoded header block ──

    /// <summary>
    /// Builds a HEADERS frame using HpackEncoder so that the decoder
    /// will see the headers after HPACK decoding.
    /// </summary>
    private static byte[] MakeHeadersFrame(
        int streamId,
        IEnumerable<(string Name, string Value)> headers,
        bool endStream = true,
        bool endHeaders = true)
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(new List<(string, string)>(headers));
        return new HeadersFrame(streamId, headerBlock, endStream, endHeaders).Serialize();
    }

    private static byte[] GoodResponse(int streamId = 1, bool endStream = true) =>
        MakeHeadersFrame(streamId, [(":status", "200")], endStream);

    // ── HV-001: Valid minimal response (status only) ──────────────────────────

    /// RFC 9113 §8.2 — Valid response with only :status is accepted
    [Fact(DisplayName = "HV-001: Valid response with only :status is accepted")]
    public void Should_Accept_When_ValidMinimalResponse()
    {
        var session = new Http2ProtocolSession();
        session.Process(GoodResponse());
        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    // ── HV-002: Valid response with :status + regular headers ─────────────────

    /// RFC 9113 §8.2 — Valid response with :status then regular headers is accepted
    [Fact(DisplayName = "HV-002: Valid response with :status then regular headers is accepted")]
    public void Should_Accept_When_StatusFollowedByRegularHeaders()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "200"), ("content-type", "text/plain")]);
        session.Process(frame);
        Assert.Single(session.Responses);
    }

    // ── HV-003: Missing :status pseudo-header ─────────────────────────────────

    /// RFC 9113 §8.2 — Missing :status pseudo-header is PROTOCOL ERROR
    [Fact(DisplayName = "HV-003: Missing :status pseudo-header is PROTOCOL_ERROR")]
    public void Should_Throw_When_StatusPseudoHeaderMissing()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [("content-type", "text/plain")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-004: Duplicate :status pseudo-header ───────────────────────────────

    /// RFC 9113 §8.2 — Duplicate :status pseudo-header is PROTOCOL ERROR
    [Fact(DisplayName = "HV-004: Duplicate :status pseudo-header is PROTOCOL_ERROR")]
    public void Should_Throw_When_StatusPseudoHeaderDuplicated()
    {
        var session = new Http2ProtocolSession();

        // Build header block manually to bypass HpackEncoder single-pass restrictions.
        // Use literal encoding to produce duplicate :status entries.
        // 0x40 = Literal Header Field with Incremental Indexing, new name
        // ":status" = 0x07, ":status"
        // "200" = 0x03, "200"
        // Repeat twice.
        var nameBytes = System.Text.Encoding.Latin1.GetBytes(":status");
        var val1Bytes = System.Text.Encoding.Latin1.GetBytes("200");
        var val2Bytes = System.Text.Encoding.Latin1.GetBytes("404");

        // Build raw header block: two literal :status entries (never-index form = 0x10)
        var block = new System.Collections.Generic.List<byte>();
        void AddLiteral(byte[] name2, byte[] value2)
        {
            block.Add(0x10);              // Literal Never Index, new name
            block.Add((byte)name2.Length);
            block.AddRange(name2);
            block.Add((byte)value2.Length);
            block.AddRange(value2);
        }

        AddLiteral(nameBytes, val1Bytes);
        AddLiteral(nameBytes, val2Bytes);

        var blockArray = block.ToArray();
        var frame = new HeadersFrame(1, blockArray.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
        Assert.Contains("Duplicate", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-005: Request pseudo-header :method in response is PROTOCOL_ERROR ──

    /// RFC 9113 §8.2 — Request pseudo-header :method in response is PROTOCOL ERROR
    [Fact(DisplayName = "HV-005: Request pseudo-header :method in response is PROTOCOL_ERROR")]
    public void Should_Throw_When_MethodPseudoHeaderInResponse()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "200"), (":method", "GET")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-006: Request pseudo-header :path in response is PROTOCOL_ERROR ────

    /// RFC 9113 §8.2 — Request pseudo-header :path in response is PROTOCOL ERROR
    [Fact(DisplayName = "HV-006: Request pseudo-header :path in response is PROTOCOL_ERROR")]
    public void Should_Throw_When_PathPseudoHeaderInResponse()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "200"), (":path", "/")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":path", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-007: Request pseudo-header :scheme in response is PROTOCOL_ERROR ──

    /// RFC 9113 §8.2 — Request pseudo-header :scheme in response is PROTOCOL ERROR
    [Fact(DisplayName = "HV-007: Request pseudo-header :scheme in response is PROTOCOL_ERROR")]
    public void Should_Throw_When_SchemePseudoHeaderInResponse()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "200"), (":scheme", "https")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":scheme", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-008: Request pseudo-header :authority in response is PROTOCOL_ERROR

    /// RFC 9113 §8.2 — Request pseudo-header :authority in response is PROTOCOL ERROR
    [Fact(DisplayName = "HV-008: Request pseudo-header :authority in response is PROTOCOL_ERROR")]
    public void Should_Throw_When_AuthorityPseudoHeaderInResponse()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "200"), (":authority", "example.com")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":authority", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-009: Unknown pseudo-header is PROTOCOL_ERROR ──────────────────────

    /// RFC 9113 §8.2 — Unknown pseudo-header is PROTOCOL ERROR
    [Fact(DisplayName = "HV-009: Unknown pseudo-header is PROTOCOL_ERROR")]
    public void Should_Throw_When_UnknownPseudoHeaderInResponse()
    {
        var session = new Http2ProtocolSession();

        var nameBytes = System.Text.Encoding.Latin1.GetBytes(":status");
        var valBytes = System.Text.Encoding.Latin1.GetBytes("200");
        var unknownName = System.Text.Encoding.Latin1.GetBytes(":custom");
        var unknownVal = System.Text.Encoding.Latin1.GetBytes("value");

        var block = new System.Collections.Generic.List<byte>();
        void AddLiteral(byte[] name2, byte[] value2)
        {
            block.Add(0x10);
            block.Add((byte)name2.Length);
            block.AddRange(name2);
            block.Add((byte)value2.Length);
            block.AddRange(value2);
        }

        AddLiteral(nameBytes, valBytes);
        AddLiteral(unknownName, unknownVal);

        var blockArray = block.ToArray();
        var frame = new HeadersFrame(1, blockArray.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":custom", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-010: Pseudo-header after regular header is PROTOCOL_ERROR ──────────

    /// RFC 9113 §8.2 — Pseudo-header :status after regular header is PROTOCOL ERROR
    [Fact(DisplayName = "HV-010: Pseudo-header :status after regular header is PROTOCOL_ERROR")]
    public void Should_Throw_When_PseudoHeaderAfterRegularHeader()
    {
        var session = new Http2ProtocolSession();

        // Encode: regular header first, then :status — violates ordering.
        var regularName = System.Text.Encoding.Latin1.GetBytes("content-type");
        var regularVal = System.Text.Encoding.Latin1.GetBytes("text/plain");
        var statusName = System.Text.Encoding.Latin1.GetBytes(":status");
        var statusVal = System.Text.Encoding.Latin1.GetBytes("200");

        var block = new System.Collections.Generic.List<byte>();
        void AddLiteral(byte[] name2, byte[] value2)
        {
            block.Add(0x10);
            block.Add((byte)name2.Length);
            block.AddRange(name2);
            block.Add((byte)value2.Length);
            block.AddRange(value2);
        }

        AddLiteral(regularName, regularVal);
        AddLiteral(statusName, statusVal);

        var blockArray = block.ToArray();
        var frame = new HeadersFrame(1, blockArray.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
        Assert.Contains("after regular header", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-011: Uppercase header name is PROTOCOL_ERROR ──────────────────────

    /// RFC 9113 §8.2 — Uppercase header name is PROTOCOL ERROR (RFC 9113 §8.2)
    [Fact(DisplayName = "HV-011: Uppercase header name is PROTOCOL_ERROR (RFC 9113 §8.2)")]
    public void Should_Throw_When_UppercaseHeaderName()
    {
        var session = new Http2ProtocolSession();

        var statusName = System.Text.Encoding.Latin1.GetBytes(":status");
        var statusVal = System.Text.Encoding.Latin1.GetBytes("200");
        var upperName = System.Text.Encoding.Latin1.GetBytes("Content-Type");
        var upperVal = System.Text.Encoding.Latin1.GetBytes("text/plain");

        var block = new System.Collections.Generic.List<byte>();
        void AddLiteral(byte[] name2, byte[] value2)
        {
            block.Add(0x10);
            block.Add((byte)name2.Length);
            block.AddRange(name2);
            block.Add((byte)value2.Length);
            block.AddRange(value2);
        }

        AddLiteral(statusName, statusVal);
        AddLiteral(upperName, upperVal);

        var blockArray = block.ToArray();
        var frame = new HeadersFrame(1, blockArray.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("uppercase", ex.Message.ToLower());
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-012: Uppercase in pseudo-header is PROTOCOL_ERROR ─────────────────

    /// RFC 9113 §8.2 — Uppercase in pseudo-header name itself is PROTOCOL ERROR
    [Fact(DisplayName = "HV-012: Uppercase in pseudo-header name itself is PROTOCOL_ERROR")]
    public void Should_Throw_When_UppercaseInPseudoHeaderName()
    {
        var session = new Http2ProtocolSession();

        var badName = System.Text.Encoding.Latin1.GetBytes(":Status");
        var val = System.Text.Encoding.Latin1.GetBytes("200");

        var block = new System.Collections.Generic.List<byte> { 0x10, (byte)badName.Length };
        block.AddRange(badName);
        block.Add((byte)val.Length);
        block.AddRange(val);

        var frame = new HeadersFrame(1, block.ToArray().AsMemory(), endStream: true, endHeaders: true).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-013: connection header is forbidden ────────────────────────────────

    /// RFC 9113 §8.2 — 'connection' header is PROTOCOL ERROR in HTTP/2
    [Fact(DisplayName = "HV-013: 'connection' header is PROTOCOL_ERROR in HTTP/2")]
    public void Should_Throw_When_ConnectionHeaderPresent()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "200"), ("connection", "keep-alive")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("connection", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-014: keep-alive header is forbidden ────────────────────────────────

    /// RFC 9113 §8.2 — 'keep-alive' header is PROTOCOL ERROR in HTTP/2
    [Fact(DisplayName = "HV-014: 'keep-alive' header is PROTOCOL_ERROR in HTTP/2")]
    public void Should_Throw_When_KeepAliveHeaderPresent()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "200"), ("keep-alive", "timeout=5")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("keep-alive", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-015: proxy-connection header is forbidden ──────────────────────────

    /// RFC 9113 §8.2 — 'proxy-connection' header is PROTOCOL ERROR in HTTP/2
    [Fact(DisplayName = "HV-015: 'proxy-connection' header is PROTOCOL_ERROR in HTTP/2")]
    public void Should_Throw_When_ProxyConnectionHeaderPresent()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "200"), ("proxy-connection", "keep-alive")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("proxy-connection", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-016: transfer-encoding header is forbidden ─────────────────────────

    /// RFC 9113 §8.2 — 'transfer-encoding' header is PROTOCOL ERROR in HTTP/2
    [Fact(DisplayName = "HV-016: 'transfer-encoding' header is PROTOCOL_ERROR in HTTP/2")]
    public void Should_Throw_When_TransferEncodingHeaderPresent()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "200"), ("transfer-encoding", "chunked")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("transfer-encoding", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-017: upgrade header is forbidden ───────────────────────────────────

    /// RFC 9113 §8.2 — 'upgrade' header is PROTOCOL ERROR in HTTP/2
    [Fact(DisplayName = "HV-017: 'upgrade' header is PROTOCOL_ERROR in HTTP/2")]
    public void Should_Throw_When_UpgradeHeaderPresent()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "200"), ("upgrade", "h2c")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("upgrade", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-018: Valid response with multiple regular headers ──────────────────

    /// RFC 9113 §8.2 — Valid response with :status and multiple regular headers is accepted
    [Fact(DisplayName = "HV-018: Valid response with :status and multiple regular headers is accepted")]
    public void Should_Accept_When_MultipleRegularHeadersAfterStatus()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1,
        [
            (":status", "200"),
            ("content-type", "application/json"),
            ("content-length", "42"),
            ("x-request-id", "abc123"),
        ]);
        session.Process(frame);
        Assert.Single(session.Responses);
    }

    // ── HV-019: Valid 404 response ────────────────────────────────────────────

    /// RFC 9113 §8.2 — Valid 404 response is accepted
    [Fact(DisplayName = "HV-019: Valid 404 response is accepted")]
    public void Should_Accept_When_Status404()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "404")]);
        session.Process(frame);
        Assert.Single(session.Responses);
        Assert.Equal(404, (int)session.Responses[0].Response.StatusCode);
    }

    // ── HV-020: Valid 301 redirect response ───────────────────────────────────

    /// RFC 9113 §8.2 — Valid 301 redirect response with location header is accepted
    [Fact(DisplayName = "HV-020: Valid 301 redirect response with location header is accepted")]
    public void Should_Accept_When_Status301WithLocationHeader()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1,
        [
            (":status", "301"),
            ("location", "https://example.com/new"),
        ]);
        session.Process(frame);
        Assert.Single(session.Responses);
    }

    // ── HV-021: Error message includes header name ────────────────────────────

    /// RFC 9113 §8.2 — PROTOCOL ERROR message for uppercase includes the offending header name
    [Fact(DisplayName = "HV-021: PROTOCOL_ERROR message for uppercase includes the offending header name")]
    public void Should_IncludeHeaderName_In_UppercaseErrorMessage()
    {
        var session = new Http2ProtocolSession();

        var statusBytes = System.Text.Encoding.Latin1.GetBytes(":status");
        var statusVal = System.Text.Encoding.Latin1.GetBytes("200");
        var badName = System.Text.Encoding.Latin1.GetBytes("X-Custom");
        var badVal = System.Text.Encoding.Latin1.GetBytes("value");

        var block = new System.Collections.Generic.List<byte>();
        void Add(byte[] n, byte[] v)
        {
            block.Add(0x10);
            block.Add((byte)n.Length);
            block.AddRange(n);
            block.Add((byte)v.Length);
            block.AddRange(v);
        }

        Add(statusBytes, statusVal);
        Add(badName, badVal);

        var frame = new HeadersFrame(1, block.ToArray().AsMemory(), endStream: true, endHeaders: true).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Contains("X-Custom", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-022: Error message for connection-specific includes header name ─────

    /// RFC 9113 §8.2 — PROTOCOL ERROR message for connection-specific includes the header name
    [Fact(DisplayName = "HV-022: PROTOCOL_ERROR message for connection-specific includes the header name")]
    public void Should_IncludeHeaderName_In_ConnectionSpecificErrorMessage()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1, [(":status", "200"), ("transfer-encoding", "chunked")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Contains("transfer-encoding", ex.Message);
        Assert.Contains("forbidden", ex.Message.ToLower());
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-023: Continuation frames — validation applies to full header block ─

    /// RFC 9113 §8.2 — Validation applies to reassembled headers from CONTINUATION frames
    [Fact(DisplayName = "HV-023: Validation applies to reassembled headers from CONTINUATION frames")]
    public void Should_Throw_When_UppercaseInContinuationHeaderBlock()
    {
        // HEADERS (no END_HEADERS) + CONTINUATION (with END_HEADERS).
        // The :status is in the HEADERS frame; the bad uppercase header is in CONTINUATION.
        var hpack1 = new HpackEncoder(useHuffman: false);
        var statusBlock = hpack1.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, statusBlock, endStream: false, endHeaders: false).Serialize();

        // Build a CONTINUATION payload with an uppercase header name.
        var badName = System.Text.Encoding.Latin1.GetBytes("X-Bad");
        var badVal = System.Text.Encoding.Latin1.GetBytes("value");
        var contBlock = new System.Collections.Generic.List<byte> { 0x10, (byte)badName.Length };
        contBlock.AddRange(badName);
        contBlock.Add((byte)badVal.Length);
        contBlock.AddRange(badVal);

        var contFrame = new ContinuationFrame(1, contBlock.ToArray(), endHeaders: true).Serialize();

        var combined = new byte[headersFrame.Length + contFrame.Length];
        headersFrame.CopyTo(combined, 0);
        contFrame.CopyTo(combined, headersFrame.Length);

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("X-Bad", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-024: Multiple streams — each validated independently ───────────────

    /// RFC 9113 §8.2 — Each stream's HEADERS block is validated independently
    [Fact(DisplayName = "HV-024: Each stream's HEADERS block is validated independently")]
    public void Should_Throw_On_SecondStream_When_SecondStreamHasMissingStatus()
    {
        var session = new Http2ProtocolSession();

        // Stream 1 is valid
        session.Process(GoodResponse(streamId: 1));

        // Stream 3 is missing :status
        var badFrame = MakeHeadersFrame(3, [("content-type", "text/plain")]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(badFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-025: 1xx informational response ───────────────────────────────────

    /// RFC 9113 §8.2 — Valid 100 Continue response (HEADERS with endStream=false) is accepted
    [Fact(DisplayName = "HV-025: Valid 100 Continue response (HEADERS with endStream=false) is accepted")]
    public void Should_Accept_When_Status100Informational()
    {
        // 1xx responses arrive as HEADERS with END_STREAM not set, so we send a DATA frame
        // to close the stream afterward.
        var session = new Http2ProtocolSession();
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "100")]);
        var headersFrame = new HeadersFrame(1, block, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, "body"u8.ToArray(), endStream: true).Serialize();
        var combined = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(combined, 0);
        dataFrame.CopyTo(combined, headersFrame.Length);
        session.Process(combined);
        // The 100 HEADERS doesn't produce a response in the usual flow (no END_STREAM).
        // A response is produced when the DATA arrives with END_STREAM.
        Assert.Single(session.Responses);
    }

    // ── HV-026: All-lowercase valid custom header ─────────────────────────────

    /// RFC 9113 §8.2 — All-lowercase custom header name is accepted
    [Fact(DisplayName = "HV-026: All-lowercase custom header name is accepted")]
    public void Should_Accept_When_AllLowercaseCustomHeader()
    {
        var session = new Http2ProtocolSession();
        var frame = MakeHeadersFrame(1,
        [
            (":status", "200"),
            ("x-custom-header", "value"),
            ("another-header", "42"),
        ]);
        session.Process(frame);
        Assert.Single(session.Responses);
    }

    // ── HV-027: Only uppercase in middle of name ──────────────────────────────

    /// RFC 9113 §8.2 — Header name with uppercase in the middle is PROTOCOL ERROR
    [Fact(DisplayName = "HV-027: Header name with uppercase in the middle is PROTOCOL_ERROR")]
    public void Should_Throw_When_UppercaseInMiddleOfHeaderName()
    {
        var session = new Http2ProtocolSession();

        var statusBytes = System.Text.Encoding.Latin1.GetBytes(":status");
        var statusVal = System.Text.Encoding.Latin1.GetBytes("200");
        var mixedName = System.Text.Encoding.Latin1.GetBytes("x-mY-Header");
        var mixedVal = System.Text.Encoding.Latin1.GetBytes("v");

        var block = new System.Collections.Generic.List<byte>();
        void Add(byte[] n, byte[] v)
        {
            block.Add(0x10);
            block.Add((byte)n.Length);
            block.AddRange(n);
            block.Add((byte)v.Length);
            block.AddRange(v);
        }

        Add(statusBytes, statusVal);
        Add(mixedName, mixedVal);

        var frame = new HeadersFrame(1, block.ToArray().AsMemory(), endStream: true, endHeaders: true).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // ── HV-028: Empty header block is PROTOCOL_ERROR (no :status) ────────────

    /// RFC 9113 §8.2 — Empty header block with no :status is PROTOCOL ERROR
    [Fact(DisplayName = "HV-028: Empty header block with no :status is PROTOCOL_ERROR")]
    public void Should_Throw_When_HeaderBlockIsEmpty()
    {
        var session = new Http2ProtocolSession();
        // Empty header block → no :status
        var frame = new HeadersFrame(1, System.ReadOnlyMemory<byte>.Empty, endStream: true, endHeaders: true).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
        Assert.True(ex.IsConnectionError);
    }
}
