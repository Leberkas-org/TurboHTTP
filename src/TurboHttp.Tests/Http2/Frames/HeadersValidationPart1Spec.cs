using System.Collections.Generic;
using TurboHttp.Protocol.Http2.Hpack;
using TurboHttp.Protocol.Http2;

namespace TurboHttp.Tests.Http2.Frames;

/// <summary>
/// Tests HTTP/2 response pseudo-header validation per RFC 9113 §8.1.2.2.
/// Verifies :status pseudo-header presence, value format, and position in header list.
/// </summary>
/// <remarks>
/// RFC 9113 §8.1.2.2: Response pseudo-headers consist of a single :status header (required).
/// Semantic validation is performed at the header-list level, not by the frame decoder.
/// </remarks>
public sealed class Http2HeadersValidationPart1Spec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void Http2FrameDecoder_should_accept_when_headers_frame_with_only_status_200()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var frame = new HeadersFrame(1, headerBlock).Serialize();

        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void Http2FrameDecoder_should_accept_when_status_100_to_599_valid()
    {
        foreach (var status in new[] { "100", "200", "301", "400", "500", "599" })
        {
            var hpack = new HpackEncoder(useHuffman: false);
            var headerBlock = hpack.Encode([(":status", status)]);
            var frame = new HeadersFrame(1, headerBlock).Serialize();

            var frames = new Http2FrameDecoder().Decode(frame);
            Assert.NotEmpty(frames);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void Http2FrameDecoder_should_accept_when_status_appears_first_in_header_list()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(
        [
            (":status", "200"),
            ("content-type", "text/plain"),
            ("content-length", "13")
        ]);
        var frame = new HeadersFrame(1, headerBlock).Serialize();

        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void Http2FrameDecoder_should_reject_when_status_appears_after_regular_header()
    {
        var block = MakeHeaderBlock(("content-type", "text/plain"), (":status", "200"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void Http2FrameDecoder_should_reject_when_multiple_status_headers()
    {
        var block = MakeHeaderBlock((":status", "200"), (":status", "201"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void Http2FrameDecoder_should_reject_when_status_missing()
    {
        var block = MakeHeaderBlock(("content-type", "text/plain"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.2")]
    public void Http2FrameDecoder_should_reject_when_unknown_pseudo_header_in_response()
    {
        var block = MakeHeaderBlock((":status", "200"), (":method", "GET"));
        var headers = DecodeBlock(block);
        var ex = Assert.Throws<Http2Exception>(() => ValidateResponseHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void Http2FrameDecoder_should_accept_when_content_encoding_header_present()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(
        [
            (":status", "200"),
            ("content-encoding", "gzip")
        ]);
        var frame = new HeadersFrame(1, headerBlock).Serialize();

        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void Http2FrameDecoder_should_accept_when_cache_control_header_present()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(
        [
            (":status", "200"),
            ("cache-control", "max-age=3600")
        ]);
        var frame = new HeadersFrame(1, headerBlock).Serialize();

        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void Http2FrameDecoder_should_accept_when_authorization_header_not_stripped_from_response()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode(
        [
            (":status", "200"),
            ("authorization", "Bearer token123")
        ]);
        var frame = new HeadersFrame(1, headerBlock).Serialize();

        var frames = new Http2FrameDecoder().Decode(frame);
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
