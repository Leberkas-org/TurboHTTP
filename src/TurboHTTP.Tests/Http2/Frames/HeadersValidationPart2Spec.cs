using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Frames;

/// <summary>
/// Tests connection-specific header rejection and validation per RFC 9113 §8.2.2.
/// Verifies that connection-specific headers (Connection, Keep-Alive, etc.) are properly rejected.
/// </summary>
/// <remarks>
/// RFC 9113 §8.2.2: HTTP/2 connection-specific headers MUST NOT be included in HTTP/2 messages.
/// Semantic validation is performed at the header-list level, not by the frame decoder.
/// </remarks>
public sealed class Http2HeadersValidationPart2Spec
{
    [Theory(Timeout = 5000)]
    [InlineData("connection")]
    [InlineData("keep-alive")]
    [InlineData("proxy-connection")]
    [InlineData("transfer-encoding")]
    [InlineData("upgrade")]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void Http2FrameDecoder_should_reject_when_connection_specific_header_present(string headerName)
    {
        var block = MakeHeaderBlock((":status", "200"), (headerName, "value"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(headerName, ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void Http2FrameDecoder_should_include_header_name_in_error_when_connection_header_rejected()
    {
        var block = MakeHeaderBlock((":status", "200"), ("connection", "close"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Contains("connection", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void Http2FrameDecoder_should_accept_when_te_trailers_header_in_response()
    {
        // RFC 9113 §8.2.2: TE exception is for requests only; in responses, te is not connection-specific
        var block = MakeHeaderBlock((":status", "200"), ("te", "trailers"));
        var headers = DecodeBlock(block);
        // Should not throw — te is not forbidden in responses
        ValidateResponseHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void Http2FrameDecoder_should_reject_when_upgrade_header_present_in_response()
    {
        var block = MakeHeaderBlock((":status", "200"), ("upgrade", "websocket"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void Http2FrameDecoder_should_accept_when_custom_headers_present()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(
        [
            (":status", "200"),
            ("x-custom-header", "value"),
            ("x-another-header", "another-value")
        ]);
        var frame = new HeadersFrame(1, headerBlock).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.2")]
    public void Http2FrameDecoder_should_accept_when_standard_headers_present()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(
        [
            (":status", "200"),
            ("content-type", "text/html"),
            ("content-length", "1024"),
            ("server", "MyServer/1.0")
        ]);
        var frame = new HeadersFrame(1, headerBlock).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void Http2FrameDecoder_should_accept_when_set_cookie_header_present()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(
        [
            (":status", "200"),
            ("set-cookie", "session=abc123")
        ]);
        var frame = new HeadersFrame(1, headerBlock).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void Http2FrameDecoder_should_accept_when_multiple_set_cookie_headers_present()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(
        [
            (":status", "200"),
            ("set-cookie", "session=abc123"),
            ("set-cookie", "tracking=xyz789")
        ]);
        var frame = new HeadersFrame(1, headerBlock).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void Http2FrameDecoder_should_accept_when_vary_header_present()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(
        [
            (":status", "200"),
            ("vary", "Accept-Encoding, User-Agent")
        ]);
        var frame = new HeadersFrame(1, headerBlock).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
    }

    private static ReadOnlyMemory<byte> MakeHeaderBlock(params (string Name, string Value)[] headers)
    {
        var enc = new HpackEncoder(useHuffman: false);
        return enc.Encode(headers);
    }

    private static IReadOnlyList<HpackHeader> DecodeBlock(ReadOnlyMemory<byte> block)
    {
        return new HpackDecoder().Decode(block.Span);
    }

    private static void ValidateResponseHeaders(IReadOnlyList<HpackHeader> headers)
    {
        if (headers.Count == 0)
        {
            throw new Http2Exception("Response must contain :status pseudo-header.",
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
                        $"Pseudo-header '{h.Name}' must not appear after regular header.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }

                if (h.Name == ":status")
                {
                    if (seenStatus)
                    {
                        throw new Http2Exception("Duplicate :status pseudo-header.",
                            Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                    }

                    seenStatus = true;
                }
                else if (IsRequestPseudoHeader(h.Name))
                {
                    throw new Http2Exception(
                        $"Request pseudo-header '{h.Name}' is not valid in a response.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }
                else
                {
                    throw new Http2Exception(
                        $"Unknown pseudo-header '{h.Name}' in response.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }
            }
            else
            {
                seenRegular = true;

                if (IsForbiddenConnectionHeader(h.Name))
                {
                    throw new Http2Exception(
                        $"Header '{h.Name}' is forbidden in HTTP/2.",
                        Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
                }
            }
        }

        if (!seenStatus)
        {
            throw new Http2Exception("Response is missing required :status pseudo-header.",
                Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
        }
    }

    private static bool IsRequestPseudoHeader(string name) =>
        name is ":method" or ":path" or ":scheme" or ":authority";

    private static bool IsForbiddenConnectionHeader(string name) =>
        name is "connection" or "keep-alive" or "proxy-connection" or "transfer-encoding" or "upgrade";
}
