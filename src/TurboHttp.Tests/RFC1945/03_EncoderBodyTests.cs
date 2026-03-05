using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// RFC 1945 §7.2 — Body encoding tests.
/// Verifies HTTP/1.0 request body encoding with Content-Length.
/// </summary>
public sealed class Http10EncoderBodyTests
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

    private static int FindSequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }
        return -1;
    }

    [Fact]
    public void Body_PostWithBody_ContentLengthIsCorrect()
    {
        const string bodyText = "Hello, World!";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent(bodyText, Encoding.ASCII)
        };

        var (_, headerLines, _) = ParseRaw(request);

        var contentLength = headerLines
            .Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal($"Content-Length: {Encoding.ASCII.GetByteCount(bodyText)}", contentLength);
    }

    [Fact]
    public void Body_PostWithBody_BodyIsCorrectlyWritten()
    {
        const string bodyText = "Hello, World!";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent(bodyText, Encoding.ASCII, "text/plain")
        };

        var (_, _, body) = ParseRaw(request);

        Assert.Equal(bodyText, Encoding.ASCII.GetString(body));
    }

    [Fact]
    public void Body_GetWithNoBody_ContentLengthAbsent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Body_GetWithNoBody_ContentTypeAbsent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Body_PostWithBinaryBody_BytesExactlyPreserved()
    {
        var bodyBytes = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x7F };
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(bodyBytes)
        };

        var buffer = MakeBuffer(8192);
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = buffer.Span[..written];

        var separator = "\r\n\r\n"u8.ToArray();
        var sepIndex = FindSequence(raw, separator);
        var actualBody = raw[(sepIndex + 4)..].ToArray();

        Assert.Equal(bodyBytes, actualBody);
    }

    [Fact]
    public void Body_PostWithEmptyBody_ContentLengthIsZero()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent([])
        };

        var (_, headerLines, body) = ParseRaw(request);

        // POST with an empty body must emit Content-Length: 0 so that HTTP/1.0 servers
        // do not wait for body data until connection-close (RFC 1945 §7.2).
        var cl = headerLines.Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Content-Length: 0", cl);
        Assert.Empty(body);
    }

    [Fact]
    public void Body_PostWithLargeBody_ContentLengthMatchesBodySize()
    {
        var largeBody = new byte[4096];
        new Random(42).NextBytes(largeBody);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(largeBody)
        };

        var buffer = MakeBuffer(16384);
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer.Span[..written]);

        var headerSection = raw[..raw.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
        var contentLengthLine = headerSection.Split("\r\n")
            .Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));

        var reportedLength = int.Parse(contentLengthLine.Split(": ")[1]);
        Assert.Equal(4096, reportedLength);
    }

    [Fact]
    public void Body_PostWithBody_BodyAppearsAfterHeaderSeparator()
    {
        const string bodyText = "BODY_CONTENT";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent(bodyText, Encoding.ASCII, "text/plain")
        };

        var raw = Encode(request);
        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var bodyPart = raw[(separatorIndex + 4)..];

        Assert.Equal(bodyText, bodyPart);
    }

    [Fact(DisplayName = "1945-enc-005: Content-Length present for POST body")]
    public void Should_SetContentLength_When_PostHasBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent("Hello!", Encoding.ASCII)
        };

        var (_, headerLines, _) = ParseRaw(request);

        var cl = headerLines.Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Content-Length: 6", cl);
    }

    [Fact(DisplayName = "1945-enc-006: Content-Length absent for bodyless GET")]
    public void Should_OmitContentLength_When_GetHasNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines,
            h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "1945-enc-008: Binary POST body encoded verbatim")]
    public void Should_EncodeBinaryBodyVerbatim_When_PostWithBinaryContent()
    {
        var bodyBytes = new byte[] { 0x00, 0x01, 0xFF, 0xFE, 0x7F, 0x80 };
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(bodyBytes)
        };

        var buffer = MakeBuffer();
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = buffer.Span[..written];

        var separator = "\r\n\r\n"u8.ToArray();
        var sepIndex = FindSequence(raw, separator);
        var actualBody = raw[(sepIndex + 4)..].ToArray();

        Assert.Equal(bodyBytes, actualBody);
    }

    [Fact(DisplayName = "1945-enc-009: UTF-8 JSON body encoded correctly")]
    public void Should_EncodeUtf8JsonBody_When_PostWithJsonContent()
    {
        const string json = "{\"name\":\"value\",\"count\":42}";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var buffer = MakeBuffer();
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = buffer.Span[..written];

        var separator = "\r\n\r\n"u8.ToArray();
        var sepIndex = FindSequence(raw, separator);
        var actualBody = Encoding.UTF8.GetString(raw[(sepIndex + 4)..]);

        Assert.Equal(json, actualBody);
    }

    [Fact(DisplayName = "enc1-body-001: Body with null bytes not truncated")]
    public void Should_NotTruncateBody_When_BodyContainsNullBytes()
    {
        var bodyBytes = new byte[] { 0x41, 0x00, 0x42, 0x00, 0x00, 0x43 };
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(bodyBytes)
        };

        var buffer = MakeBuffer();
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = buffer.Span[..written];

        var separator = "\r\n\r\n"u8.ToArray();
        var sepIndex = FindSequence(raw, separator);
        var actualBody = raw[(sepIndex + 4)..].ToArray();

        Assert.Equal(bodyBytes.Length, actualBody.Length);
        Assert.Equal(bodyBytes, actualBody);
    }

    [Fact(DisplayName = "enc1-body-002: 2 MB body encoded with correct Content-Length")]
    public void Should_EncodeWithCorrectContentLength_When_BodyIs2MB()
    {
        var bodyBytes = new byte[2 * 1024 * 1024];
        new Random(42).NextBytes(bodyBytes);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(bodyBytes)
        };

        var buffer = MakeBuffer(3 * 1024 * 1024);
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer.Span[..written]);

        var headerSection = raw[..raw.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
        var clLine = headerSection.Split("\r\n")
            .Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var reportedLength = int.Parse(clLine.Split(": ")[1]);

        Assert.Equal(2 * 1024 * 1024, reportedLength);
    }

    [Fact(DisplayName = "enc1-body-003: CRLFCRLF separates headers from body")]
    public void Should_SeparateHeadersFromBody_When_EncodingWithBody()
    {
        const string body = "BODY";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent(body, Encoding.ASCII)
        };

        var raw = Encode(request);
        var sepIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        Assert.True(sepIndex > 0, "Header-body separator \\r\\n\\r\\n must be present");
        Assert.Equal(body, raw[(sepIndex + 4)..]);
    }
}
