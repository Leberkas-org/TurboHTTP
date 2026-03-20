using System.IO;
using System.Text;
using TurboHttp.Protocol.RFC1945;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// Tests HTTP/1.0 entity body serialization per RFC 1945 §7.
/// Verifies that request bodies are appended after the header section.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Encoder"/>.
/// RFC 1945 §7: Entity body transmitted with HTTP requests.
/// </remarks>
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

    [Fact(DisplayName = "RFC1945-7.2-BD-001: POST body Content-Length is correct")]
    public void Should_SetCorrectContentLength_When_PostHasBody()
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

    [Fact(DisplayName = "RFC1945-7.2-BD-002: POST body is correctly written")]
    public void Should_WriteBodyCorrectly_When_PostHasBody()
    {
        const string bodyText = "Hello, World!";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent(bodyText, Encoding.ASCII, "text/plain")
        };

        var (_, _, body) = ParseRaw(request);

        Assert.Equal(bodyText, Encoding.ASCII.GetString(body));
    }

    [Fact(DisplayName = "RFC1945-7.2-BD-003: GET with no body has no Content-Length")]
    public void Should_NotIncludeContentLength_When_GetRequestHasNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-7.2-BD-004: GET with no body has no Content-Type")]
    public void Should_OmitContentType_When_GetHasNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-7.2-BD-005: Binary POST body bytes exactly preserved")]
    public void Should_PreserveBinaryBytes_When_PostWithBinaryBody()
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

    [Fact(DisplayName = "RFC1945-7.2-BD-006: Empty POST body has Content-Length 0")]
    public void Should_SetContentLengthToZero_When_PostWithEmptyBody()
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

    [Fact(DisplayName = "RFC1945-7.2-BD-007: Large POST body Content-Length matches body size")]
    public void Should_MatchContentLengthToBodySize_When_PostWithLargeBody()
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

    [Fact(DisplayName = "RFC1945-7.2-BD-008: Body appears after header separator")]
    public void Should_PlaceBodyAfterHeaderSeparator_When_PostWithBody()
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

    [Fact(DisplayName = "RFC1945-7.2-BD-009: Content-Length present for POST body")]
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

    [Fact(DisplayName = "RFC1945-7.2-BD-010: Content-Length absent for bodyless GET")]
    public void Should_OmitContentLength_When_GetHasNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines,
            h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "RFC1945-7.2-BD-011: Binary POST body encoded verbatim")]
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

    [Fact(DisplayName = "RFC1945-7.2-BD-012: UTF-8 JSON body encoded correctly")]
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

    [Fact(DisplayName = "RFC1945-7.2-BD-013: Body with null bytes not truncated")]
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

    [Fact(DisplayName = "RFC1945-7.2-BD-014: 2 MB body encoded with correct Content-Length")]
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

    [Fact(DisplayName = "RFC1945-7.2-BD-015: CRLFCRLF separates headers from body")]
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

    [Fact(DisplayName = "RFC1945-7.2-BD-016: Streaming content body encoded without .Result deadlock")]
    public void Should_EncodeStreamingContent_When_ContentIsReadableStream()
    {
        const string bodyText = "streaming body content";
        var bodyBytes = Encoding.ASCII.GetBytes(bodyText);

        // Use StreamContent to simulate a streaming (non-buffered) HttpContent
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StreamContent(new MemoryStream(bodyBytes))
        };

        var buffer = MakeBuffer();
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = buffer.Span[..written];

        var separator = "\r\n\r\n"u8.ToArray();
        var sepIndex = FindSequence(raw, separator);
        var actualBody = Encoding.ASCII.GetString(raw[(sepIndex + 4)..]);

        Assert.Equal(bodyText, actualBody);
    }
}
