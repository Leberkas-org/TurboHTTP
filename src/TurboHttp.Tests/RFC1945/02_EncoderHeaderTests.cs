using System.Net.Http.Headers;
using System.Text;
using TurboHttp.Protocol.RFC1945;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// Tests HTTP/1.0 header field serialization per RFC 1945 §4.2.
/// Verifies name-value pairs, folding rules, and header ordering.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Encoder"/>.
/// RFC 1945 §4.2: Message headers — field-name ':' field-value CRLF.
/// </remarks>
public sealed class Http10EncoderHeaderTests
{
    private static Memory<byte> MakeBuffer(int size = 8192) => new byte[size];

    private static (string requestLine, string[] headerLines, byte[] body) ParseRaw(HttpRequestMessage request,
        int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer.Span[..written]);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerSection = raw[..separatorIndex];
        var bodyString = raw[(separatorIndex + 4)..];

        var lines = headerSection.Split("\r\n");
        var requestLine = lines[0];
        var headerLines = lines[1..];

        return (requestLine, headerLines, Encoding.ASCII.GetBytes(bodyString));
    }

    private static string Encode(HttpRequestMessage request, int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Http10Encoder.Encode(request, ref buffer);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-001: Host header removed for HTTP/1.0")]
    public void Should_RemoveHostHeader_When_ProtocolIsHttp10()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-002: Connection header removed")]
    public void Should_RemoveConnectionHeader_When_EncodingHttp10()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-003: Keep-Alive header removed")]
    public void Should_RemoveKeepAliveHeader_When_EncodingHttp10()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Keep-Alive:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-004: Transfer-Encoding header removed")]
    public void Should_RemoveTransferEncodingHeader_When_EncodingHttp10()
    {
        // Transfer-Encoding ist HTTP/1.1 (RFC 2616 §14.41)
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-005: Custom header preserved")]
    public void Should_PreserveCustomHeader_When_Encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Custom-Header", "my-value");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h == "X-Custom-Header: my-value");
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-006: Multiple custom headers all preserved")]
    public void Should_PreserveAllCustomHeaders_When_MultiplePresent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Header-A", "value-a");
        request.Headers.TryAddWithoutValidation("X-Header-B", "value-b");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h == "X-Header-A: value-a");
        Assert.Contains(headerLines, h => h == "X-Header-B: value-b");
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-007: Header format is name: value")]
    public void Should_FormatAsNameColonSpaceValue_When_EncodingHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "test-value");

        var (_, headerLines, _) = ParseRaw(request);

        var header = headerLines.Single(h => h.StartsWith("X-Test:"));
        Assert.Equal("X-Test: test-value", header);
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-008: Each header ends with CRLF")]
    public void Should_EndEachHeaderWithCrLf_When_Encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "value");

        var raw = Encode(request);

        var headerSection = raw[..raw.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
        foreach (var line in headerSection.Split("\r\n").Skip(1))
        {
            Assert.Contains(line + "\r\n", raw);
        }
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-009: Multi-value header each value on separate line")]
    public void Should_EmitEachValueOnSeparateLine_When_MultiValueHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var (_, headerLines, _) = ParseRaw(request);

        var acceptLines = headerLines.Where(h => h.StartsWith("Accept:", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(2, acceptLines.Length);
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-010: Accept header preserved")]
    public void Should_PreserveAcceptHeader_When_Encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h.StartsWith("Accept:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-011: Request with no custom headers has no suppressed headers")]
    public void Should_ContainOnlyMandatoryHeaders_When_NoCustomHeadersPresent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(headerLines, h => h.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(headerLines, h => h.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-012: Header separator is double CRLF")]
    public void Should_UseDoubleCrLfSeparator_When_EncodingHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var raw = Encode(request);

        Assert.Contains("\r\n\r\n", raw);
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-013: Host header absent in HTTP/1.0 request")]
    public void Should_OmitHostHeader_When_EncodingHttp10()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-014: Transfer-Encoding absent in HTTP/1.0 request")]
    public void Should_OmitTransferEncoding_When_EncodingHttp10()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked");
        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines,
            h => h.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-015: Connection header absent in HTTP/1.0 request")]
    public void Should_OmitConnectionHeader_When_EncodingHttp10()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines,
            h => h.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-016: Every header line terminated with CRLF")]
    public void Should_TerminateEveryHeaderWithCrlf_When_Encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-One", "1");
        request.Headers.TryAddWithoutValidation("X-Two", "2");

        var raw = Encode(request);
        var headerSection = raw[..raw.IndexOf("\r\n\r\n", StringComparison.Ordinal)];

        Assert.DoesNotContain("\n", headerSection.Replace("\r\n", ""));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-017: Custom header name casing preserved")]
    public void Should_PreserveHeaderNameCasing_When_Encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-My-Custom-Header", "value");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h.StartsWith("X-My-Custom-Header:"));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-018: Multiple custom headers all emitted")]
    public void Should_EmitAllCustomHeaders_When_MultiplePresent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-First", "a");
        request.Headers.TryAddWithoutValidation("X-Second", "b");
        request.Headers.TryAddWithoutValidation("X-Third", "c");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h == "X-First: a");
        Assert.Contains(headerLines, h => h == "X-Second: b");
        Assert.Contains(headerLines, h => h == "X-Third: c");
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-019: Semicolon in header value preserved verbatim")]
    public void Should_PreserveSemicolon_When_InHeaderValue()
    {
        var content = new ByteArrayContent("x"u8.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", "text/html; charset=utf-8");
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = content
        };

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines,
            h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)
                 && h.Contains(";"));
    }

    [Fact(DisplayName = "RFC1945-4.2-HD-020: NUL byte in header value throws exception")]
    public void Should_ThrowArgumentException_When_HeaderValueContainsNul()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Bad", "value\0evil");

        var buffer = MakeBuffer();

        Assert.Throws<ArgumentException>(() => Http10Encoder.Encode(request, ref buffer));
    }
}
