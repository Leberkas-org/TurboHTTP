using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

public sealed class Http10DecoderHeaderTests
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        string body = "")
    {
        var raw = $"{statusLine}\r\n{headers}\r\n\r\n{body}";
        return Bytes(raw);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-001: Single header parsed correctly")]
    public void Headers_SingleHeader_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Type: text/plain\r\nContent-Length: 5", "Hello");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.NotNull(response);
        Assert.NotNull(response.Content);

        Assert.True(response.Content.Headers.TryGetValues("Content-Type", out var values));
        Assert.Contains("text/plain", values);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-002: Custom header parsed correctly")]
    public void Headers_CustomHeader_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Custom-Header: my-value\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Custom-Header", out var values));
        Assert.Contains("my-value", values);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-003: Multiple custom headers all parsed")]
    public void Headers_MultipleCustomHeaders_AllParsed()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Header-A: value-a\r\nX-Header-B: value-b\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Header-A", out var a));
        Assert.True(response.Headers.TryGetValues("X-Header-B", out var b));
        Assert.Contains("value-a", a);
        Assert.Contains("value-b", b);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-004: Header names are case-insensitive")]
    public void Headers_NamesAreCaseInsensitive()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "x-custom-header: lower-case\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Custom-Header", out var values)
                    || response.Headers.TryGetValues("x-custom-header", out values));
        Assert.Contains("lower-case", values);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-005: Obs-fold continuation accepted")]
    public void Headers_FoldedHeader_IsContinuedCorrectly()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Folded: first part\r\n continued\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Folded", out var values));

        var combined = string.Join(" ", values);
        Assert.Contains("first part", combined);
        Assert.Contains("continued", combined);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-006: Header with leading/trailing spaces trimmed")]
    public void Headers_HeaderWithLeadingTrailingSpaces_AreTrimmed()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Spaced:   trimmed-value   \r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Spaced", out var values));
        Assert.Contains("trimmed-value", values);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-007: LF-only line endings accepted in headers")]
    public void Headers_LfOnlyLineEnding_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\nX-Lf-Header: lf-value\nContent-Length: 0\n\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-008: Obs-fold with multiple continuation lines merged")]
    public void Should_MergeDoubleObsFold_When_TwoContinuationLines()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Multi: part1\r\n part2\r\n part3\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Multi", out var values));
        var combined = string.Join("", values);
        Assert.Contains("part1", combined);
        Assert.Contains("part2", combined);
        Assert.Contains("part3", combined);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-009: Duplicate response headers both accessible")]
    public void Should_PreserveBothHeaders_When_DuplicateNonContentLength()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Dup: first\r\nX-Dup: second\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Dup", out var values));
        var list = values.ToList();
        Assert.Contains("first", list);
        Assert.Contains("second", list);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-010: Header without colon causes parse error")]
    public void Should_ThrowInvalidHeader_When_NoColon()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nBadHeaderNoColon\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecodeError.InvalidHeader, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-011: Case-insensitive Content-Length header matching")]
    public void Should_MatchCaseInsensitive_When_UppercaseHeaderName()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nCONTENT-LENGTH: 5\r\n\r\nHello";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(5, response!.Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-012: Header value whitespace trimmed")]
    public void Should_TrimWhitespace_When_HeaderValueHasExtraSpaces()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Trimmed:    hello world   \r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Trimmed", out var values));
        Assert.Equal("hello world", values.First());
    }

    [Fact(DisplayName = "RFC1945-4-HDR-013: Space in header name causes parse error")]
    public void Should_ThrowInvalidFieldName_When_SpaceInHeaderName()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nBad Name: value\r\nContent-Length: 0\r\n\r\n";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecodeError.InvalidFieldName, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-014: Tab character in header value accepted")]
    public void Should_AcceptTab_When_HeaderValueContainsTab()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Tab: before\tafter\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.True(response!.Headers.TryGetValues("X-Tab", out var values));
        Assert.Contains("before\tafter", values);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-015: Response with no headers except status-line accepted")]
    public void Should_AcceptResponse_When_ZeroHeaders()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-4-HDR-016: Empty header value skipped safely")]
    public void EdgeCase_HeaderWithoutValue_SkippedSafely()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }
}
