using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2DecoderHeadersValidationTests
{

    /// <summary>
    /// Encodes a set of headers using HpackEncoder (no Huffman) and returns the block as Memory.
    /// </summary>
    private static ReadOnlyMemory<byte> MakeHeaderBlock(params (string Name, string Value)[] headers)
    {
        var enc = new HpackEncoder(useHuffman: false);
        return enc.Encode(headers);
    }

    /// <summary>
    /// Decodes a raw HPACK header block using a fresh HpackDecoder.
    /// </summary>
    private static IReadOnlyList<HpackHeader> DecodeBlock(ReadOnlyMemory<byte> block)
    {
        return new HpackDecoder().Decode(block.Span);
    }

    /// <summary>
    /// RFC 9113 §8.2 / §8.3: Validates that decoded response headers satisfy all
    /// HTTP/2 header field validity requirements. Throws Http2Exception on violation.
    /// </summary>
    private static void ValidateResponseHeaders(IReadOnlyList<HpackHeader> headers)
    {
        if (headers.Count == 0)
        {
            throw new Http2Exception(
                "RFC 9113 §8.3.2: Response HEADERS block is missing the required :status pseudo-header.",
                Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
        }

        var seenRegular = false;
        var seenStatus = false;

        foreach (var h in headers)
        {
            if (h.Name.StartsWith(':'))
            {
                if (seenRegular)
                {
                    throw new Http2Exception(
                        $"RFC 9113 §8.3: Pseudo-header '{h.Name}' must not appear after regular header.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }

                foreach (var c in h.Name)
                {
                    if (char.IsUpper(c))
                    {
                        throw new Http2Exception(
                            $"RFC 9113 §8.2: Pseudo-header name '{h.Name}' contains uppercase characters.",
                            Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                    }
                }

                if (h.Name == ":status")
                {
                    if (seenStatus)
                    {
                        throw new Http2Exception(
                            "RFC 9113 §8.3.2: Duplicate :status pseudo-header.",
                            Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                    }

                    seenStatus = true;
                }
                else if (IsRequestPseudoHeader(h.Name))
                {
                    throw new Http2Exception(
                        $"RFC 9113 §8.3.2: Request pseudo-header '{h.Name}' is not valid in a response.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }
                else
                {
                    throw new Http2Exception(
                        $"RFC 9113 §8.3: Unknown pseudo-header '{h.Name}' in response.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }
            }
            else
            {
                seenRegular = true;

                foreach (var c in h.Name)
                {
                    if (char.IsUpper(c))
                    {
                        throw new Http2Exception(
                            $"RFC 9113 §8.2: Header field name '{h.Name}' contains uppercase characters; all names must be lowercase.",
                            Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                    }
                }

                if (IsForbiddenConnectionHeader(h.Name))
                {
                    throw new Http2Exception(
                        $"RFC 9113 §8.2.2: Header '{h.Name}' is forbidden in HTTP/2.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }
            }
        }

        if (!seenStatus)
        {
            throw new Http2Exception(
                "RFC 9113 §8.3.2: Response HEADERS block is missing the required :status pseudo-header.",
                Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
        }
    }

    private static bool IsRequestPseudoHeader(string name) =>
        name is ":method" or ":path" or ":scheme" or ":authority";

    private static bool IsForbiddenConnectionHeader(string name) =>
        name is "connection" or "keep-alive" or "proxy-connection" or "transfer-encoding" or "upgrade";


    /// RFC 9113 §8.3 — Valid response with only :status is accepted
    [Fact(DisplayName = "RFC9113-8.3-HV-001: Valid response with only :status is accepted")]
    public void Should_Accept_When_ValidMinimalResponse()
    {
        var block = MakeHeaderBlock((":status", "200"));
        var headers = DecodeBlock(block);
        ValidateResponseHeaders(headers); // must not throw
        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "200");
    }


    /// RFC 9113 §8.3 — Valid response with :status then regular headers is accepted
    [Fact(DisplayName = "RFC9113-8.3-HV-002: Valid response with :status then regular headers is accepted")]
    public void Should_Accept_When_StatusFollowedByRegularHeaders()
    {
        var block = MakeHeaderBlock((":status", "200"), ("content-type", "text/plain"));
        var headers = DecodeBlock(block);
        ValidateResponseHeaders(headers); // must not throw
        Assert.Contains(headers, h => h.Name == "content-type");
    }


    /// RFC 9113 §8.3 — Missing :status pseudo-header is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.3-HV-003: Missing :status pseudo-header is PROTOCOL_ERROR")]
    public void Should_Throw_When_StatusPseudoHeaderMissing()
    {
        var block = MakeHeaderBlock(("content-type", "text/plain"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.3 — Duplicate :status pseudo-header is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.3-HV-004: Duplicate :status pseudo-header is PROTOCOL_ERROR")]
    public void Should_Throw_When_StatusPseudoHeaderDuplicated()
    {
        // Build raw header block with two literal :status entries (never-index form = 0x10).
        var nameBytes = System.Text.Encoding.Latin1.GetBytes(":status");
        var val1Bytes = System.Text.Encoding.Latin1.GetBytes("200");
        var val2Bytes = System.Text.Encoding.Latin1.GetBytes("404");

        var block = new List<byte>();
        void AddLiteral(byte[] name, byte[] value)
        {
            block.Add(0x10);
            block.Add((byte)name.Length);
            block.AddRange(name);
            block.Add((byte)value.Length);
            block.AddRange(value);
        }

        AddLiteral(nameBytes, val1Bytes);
        AddLiteral(nameBytes, val2Bytes);

        var headers = new HpackDecoder().Decode(block.ToArray().AsSpan());
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
        Assert.Contains("Duplicate", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.3 — Request pseudo-header :method in response is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.3-HV-005: Request pseudo-header :method in response is PROTOCOL_ERROR")]
    public void Should_Throw_When_MethodPseudoHeaderInResponse()
    {
        var block = MakeHeaderBlock((":status", "200"), (":method", "GET"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.3 — Request pseudo-header :path in response is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.3-HV-006: Request pseudo-header :path in response is PROTOCOL_ERROR")]
    public void Should_Throw_When_PathPseudoHeaderInResponse()
    {
        var block = MakeHeaderBlock((":status", "200"), (":path", "/"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":path", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.3 — Request pseudo-header :scheme in response is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.3-HV-007: Request pseudo-header :scheme in response is PROTOCOL_ERROR")]
    public void Should_Throw_When_SchemePseudoHeaderInResponse()
    {
        var block = MakeHeaderBlock((":status", "200"), (":scheme", "https"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":scheme", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.3 — Request pseudo-header :authority in response is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.3-HV-008: Request pseudo-header :authority in response is PROTOCOL_ERROR")]
    public void Should_Throw_When_AuthorityPseudoHeaderInResponse()
    {
        var block = MakeHeaderBlock((":status", "200"), (":authority", "example.com"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":authority", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.3 — Unknown pseudo-header is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.3-HV-009: Unknown pseudo-header in response is PROTOCOL_ERROR")]
    public void Should_Throw_When_UnknownPseudoHeaderInResponse()
    {
        var nameBytes = System.Text.Encoding.Latin1.GetBytes(":status");
        var valBytes = System.Text.Encoding.Latin1.GetBytes("200");
        var unknownName = System.Text.Encoding.Latin1.GetBytes(":custom");
        var unknownVal = System.Text.Encoding.Latin1.GetBytes("value");

        var block = new List<byte>();
        void AddLiteral(byte[] name, byte[] value)
        {
            block.Add(0x10);
            block.Add((byte)name.Length);
            block.AddRange(name);
            block.Add((byte)value.Length);
            block.AddRange(value);
        }

        AddLiteral(nameBytes, valBytes);
        AddLiteral(unknownName, unknownVal);

        var headers = new HpackDecoder().Decode(block.ToArray().AsSpan());
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":custom", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.3 — Pseudo-header :status after regular header is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.3-HV-010: Pseudo-header :status after regular header is PROTOCOL_ERROR")]
    public void Should_Throw_When_PseudoHeaderAfterRegularHeader()
    {
        var regularName = System.Text.Encoding.Latin1.GetBytes("content-type");
        var regularVal = System.Text.Encoding.Latin1.GetBytes("text/plain");
        var statusName = System.Text.Encoding.Latin1.GetBytes(":status");
        var statusVal = System.Text.Encoding.Latin1.GetBytes("200");

        var block = new List<byte>();
        void AddLiteral(byte[] name, byte[] value)
        {
            block.Add(0x10);
            block.Add((byte)name.Length);
            block.AddRange(name);
            block.Add((byte)value.Length);
            block.AddRange(value);
        }

        AddLiteral(regularName, regularVal);
        AddLiteral(statusName, statusVal);

        var headers = new HpackDecoder().Decode(block.ToArray().AsSpan());
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
        Assert.Contains("after regular header", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2 — Uppercase header name is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-HV-011: Uppercase header name is PROTOCOL_ERROR")]
    public void Should_Throw_When_UppercaseHeaderName()
    {
        var statusName = System.Text.Encoding.Latin1.GetBytes(":status");
        var statusVal = System.Text.Encoding.Latin1.GetBytes("200");
        var upperName = System.Text.Encoding.Latin1.GetBytes("Content-Type");
        var upperVal = System.Text.Encoding.Latin1.GetBytes("text/plain");

        var block = new List<byte>();
        void AddLiteral(byte[] name, byte[] value)
        {
            block.Add(0x10);
            block.Add((byte)name.Length);
            block.AddRange(name);
            block.Add((byte)value.Length);
            block.AddRange(value);
        }

        AddLiteral(statusName, statusVal);
        AddLiteral(upperName, upperVal);

        var headers = new HpackDecoder().Decode(block.ToArray().AsSpan());
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("uppercase", ex.Message.ToLower());
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2 — Uppercase in pseudo-header name is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-HV-012: Uppercase in pseudo-header name is PROTOCOL_ERROR")]
    public void Should_Throw_When_UppercaseInPseudoHeaderName()
    {
        var badName = System.Text.Encoding.Latin1.GetBytes(":Status");
        var val = System.Text.Encoding.Latin1.GetBytes("200");

        var block = new List<byte> { 0x10, (byte)badName.Length };
        block.AddRange(badName);
        block.Add((byte)val.Length);
        block.AddRange(val);

        var headers = new HpackDecoder().Decode(block.ToArray().AsSpan());
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2.2 — 'connection' header is PROTOCOL_ERROR in HTTP/2
    [Fact(DisplayName = "RFC9113-8.2.2-HV-013: 'connection' header is PROTOCOL_ERROR in HTTP/2")]
    public void Should_Throw_When_ConnectionHeaderPresent()
    {
        var block = MakeHeaderBlock((":status", "200"), ("connection", "keep-alive"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("connection", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2.2 — 'keep-alive' header is PROTOCOL_ERROR in HTTP/2
    [Fact(DisplayName = "RFC9113-8.2.2-HV-014: 'keep-alive' header is PROTOCOL_ERROR in HTTP/2")]
    public void Should_Throw_When_KeepAliveHeaderPresent()
    {
        var block = MakeHeaderBlock((":status", "200"), ("keep-alive", "timeout=5"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("keep-alive", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2.2 — 'proxy-connection' header is PROTOCOL_ERROR in HTTP/2
    [Fact(DisplayName = "RFC9113-8.2.2-HV-015: 'proxy-connection' header is PROTOCOL_ERROR in HTTP/2")]
    public void Should_Throw_When_ProxyConnectionHeaderPresent()
    {
        var block = MakeHeaderBlock((":status", "200"), ("proxy-connection", "keep-alive"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("proxy-connection", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2.2 — 'transfer-encoding' header is PROTOCOL_ERROR in HTTP/2
    [Fact(DisplayName = "RFC9113-8.2.2-HV-016: 'transfer-encoding' header is PROTOCOL_ERROR in HTTP/2")]
    public void Should_Throw_When_TransferEncodingHeaderPresent()
    {
        var block = MakeHeaderBlock((":status", "200"), ("transfer-encoding", "chunked"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("transfer-encoding", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2.2 — 'upgrade' header is PROTOCOL_ERROR in HTTP/2
    [Fact(DisplayName = "RFC9113-8.2.2-HV-017: 'upgrade' header is PROTOCOL_ERROR in HTTP/2")]
    public void Should_Throw_When_UpgradeHeaderPresent()
    {
        var block = MakeHeaderBlock((":status", "200"), ("upgrade", "h2c"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("upgrade", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2 — Valid response with :status and multiple regular headers is accepted
    [Fact(DisplayName = "RFC9113-8.2-HV-018: Valid response with :status and multiple regular headers is accepted")]
    public void Should_Accept_When_MultipleRegularHeadersAfterStatus()
    {
        var block = MakeHeaderBlock(
            (":status", "200"),
            ("content-type", "application/json"),
            ("content-length", "42"),
            ("x-request-id", "abc123"));

        var headers = DecodeBlock(block);
        ValidateResponseHeaders(headers); // must not throw
        Assert.Equal(4, headers.Count);
    }


    /// RFC 9113 §8.3 — Valid 404 response is accepted
    [Fact(DisplayName = "RFC9113-8.3-HV-019: Valid 404 response is accepted")]
    public void Should_Accept_When_Status404()
    {
        var block = MakeHeaderBlock((":status", "404"));
        var headers = DecodeBlock(block);
        ValidateResponseHeaders(headers); // must not throw
        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "404");
    }


    /// RFC 9113 §8.3 — Valid 301 redirect response with location header is accepted
    [Fact(DisplayName = "RFC9113-8.3-HV-020: Valid 301 redirect response with location header is accepted")]
    public void Should_Accept_When_Status301WithLocationHeader()
    {
        var block = MakeHeaderBlock((":status", "301"), ("location", "https://example.com/new"));
        var headers = DecodeBlock(block);
        ValidateResponseHeaders(headers); // must not throw
        Assert.Contains(headers, h => h.Name == "location");
    }


    /// RFC 9113 §8.2 — PROTOCOL_ERROR message for uppercase includes the offending header name
    [Fact(DisplayName = "RFC9113-8.2-HV-021: PROTOCOL_ERROR message for uppercase includes the offending header name")]
    public void Should_IncludeHeaderName_In_UppercaseErrorMessage()
    {
        var statusBytes = System.Text.Encoding.Latin1.GetBytes(":status");
        var statusVal = System.Text.Encoding.Latin1.GetBytes("200");
        var badName = System.Text.Encoding.Latin1.GetBytes("X-Custom");
        var badVal = System.Text.Encoding.Latin1.GetBytes("value");

        var block = new List<byte>();
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

        var headers = new HpackDecoder().Decode(block.ToArray().AsSpan());
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Contains("X-Custom", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2.2 — PROTOCOL_ERROR message for connection-specific includes the header name
    [Fact(DisplayName = "RFC9113-8.2.2-HV-022: PROTOCOL_ERROR message for connection-specific header includes name and 'forbidden'")]
    public void Should_IncludeHeaderName_In_ConnectionSpecificErrorMessage()
    {
        var block = MakeHeaderBlock((":status", "200"), ("transfer-encoding", "chunked"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Contains("transfer-encoding", ex.Message);
        Assert.Contains("forbidden", ex.Message.ToLower());
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2 — Validation applies to reassembled header block from CONTINUATION frames
    [Fact(DisplayName = "RFC9113-8.2-HV-023: Validation applies to reassembled headers from CONTINUATION frames")]
    public void Should_Throw_When_UppercaseInContinuationHeaderBlock()
    {
        // HEADERS (no END_HEADERS) carries :status 200.
        // CONTINUATION (END_HEADERS) carries an uppercase header name — violates §8.2.
        var enc = new HpackEncoder(useHuffman: false);
        var statusBlock = enc.Encode([(":status", "200")]).ToArray();
        var headersFrame = new HeadersFrame(1, statusBlock.AsMemory(), endStream: false, endHeaders: false);

        // Build CONTINUATION payload with an uppercase header name via raw literal encoding.
        var badName = System.Text.Encoding.Latin1.GetBytes("X-Bad");
        var badVal = System.Text.Encoding.Latin1.GetBytes("value");
        var contBlock = new List<byte> { 0x10, (byte)badName.Length };
        contBlock.AddRange(badName);
        contBlock.Add((byte)badVal.Length);
        contBlock.AddRange(badVal);

        // Assemble the full header block from HEADERS + CONTINUATION fragments.
        var fullBlock = new byte[statusBlock.Length + contBlock.Count];
        statusBlock.CopyTo(fullBlock, 0);
        contBlock.ToArray().CopyTo(fullBlock, statusBlock.Length);

        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("X-Bad", ex.Message);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2 — Each stream's header block is validated independently
    [Fact(DisplayName = "RFC9113-8.2-HV-024: Each stream's header block is validated independently")]
    public void Should_Throw_On_SecondStream_When_SecondStreamHasMissingStatus()
    {
        // Stream 1: valid response.
        var block1 = MakeHeaderBlock((":status", "200"));
        var headers1 = DecodeBlock(block1);
        ValidateResponseHeaders(headers1); // must not throw

        // Stream 3: missing :status → PROTOCOL_ERROR.
        var block3 = MakeHeaderBlock(("content-type", "text/plain"));
        var headers3 = DecodeBlock(block3);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers3));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.3 — Valid :status 100 (1xx informational) is accepted
    [Fact(DisplayName = "RFC9113-8.3-HV-025: Valid 100 Continue response (:status 100) is accepted")]
    public void Should_Accept_When_Status100Informational()
    {
        var block = MakeHeaderBlock((":status", "100"));
        var headers = DecodeBlock(block);
        ValidateResponseHeaders(headers); // must not throw
        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "100");
    }


    /// RFC 9113 §8.2 — All-lowercase custom header names are accepted
    [Fact(DisplayName = "RFC9113-8.2-HV-026: All-lowercase custom header names are accepted")]
    public void Should_Accept_When_AllLowercaseCustomHeader()
    {
        var block = MakeHeaderBlock(
            (":status", "200"),
            ("x-custom-header", "value"),
            ("another-header", "42"));

        var headers = DecodeBlock(block);
        ValidateResponseHeaders(headers); // must not throw
        Assert.Contains(headers, h => h.Name == "x-custom-header");
    }


    /// RFC 9113 §8.2 — Header name with uppercase in the middle is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-HV-027: Header name with uppercase in the middle is PROTOCOL_ERROR")]
    public void Should_Throw_When_UppercaseInMiddleOfHeaderName()
    {
        var statusBytes = System.Text.Encoding.Latin1.GetBytes(":status");
        var statusVal = System.Text.Encoding.Latin1.GetBytes("200");
        var mixedName = System.Text.Encoding.Latin1.GetBytes("x-mY-Header");
        var mixedVal = System.Text.Encoding.Latin1.GetBytes("v");

        var block = new List<byte>();
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

        var headers = new HpackDecoder().Decode(block.ToArray().AsSpan());
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.3 — Empty header block (no :status) is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.3-HV-028: Empty header block with no :status is PROTOCOL_ERROR")]
    public void Should_Throw_When_HeaderBlockIsEmpty()
    {
        // Decode an empty block — no headers at all.
        var headers = new HpackDecoder().Decode(ReadOnlySpan<byte>.Empty);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
        Assert.True(ex.IsConnectionError);
    }
}
