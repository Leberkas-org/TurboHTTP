using System.Text;
using Encoder = TurboHTTP.Protocol.Http10.Encoder;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10EncoderBodySpec
{
    private static Span<byte> MakeBuffer(int size = 8192) => new byte[size];

    private static (string[] headerLines, byte[] body) ParseRaw(HttpRequestMessage request,
        int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer[..written]);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerSection = raw[..separatorIndex];
        var bodyString = raw[(separatorIndex + 4)..];

        var lines = headerSection.Split("\r\n");
        var headerLines = lines[1..];

        return (headerLines, Encoding.ASCII.GetBytes(bodyString));
    }

    private static string Encode(HttpRequestMessage request, int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Encoder.Encode(request, ref buffer);
        return Encoding.ASCII.GetString(buffer[..written]);
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_set_correct_content_length()
    {
        const string bodyText = "Hello, World!";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent(bodyText, Encoding.ASCII)
        };

        var (headerLines, _) = ParseRaw(request);

        var contentLength = headerLines
            .Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal($"Content-Length: {Encoding.ASCII.GetByteCount(bodyText)}", contentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_write_body_correctly()
    {
        const string bodyText = "Hello, World!";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent(bodyText, Encoding.ASCII, "text/plain")
        };

        var (_, body) = ParseRaw(request);

        Assert.Equal(bodyText, Encoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_not_include_content_length_when_get_request_has_no_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_omit_content_type_when_get_has_no_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_preserve_binary_bytes()
    {
        var bodyBytes = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x7F };
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(bodyBytes)
        };

        var buffer = MakeBuffer();
        var written = Encoder.Encode(request, ref buffer);
        var raw = buffer[..written];

        var separator = "\r\n\r\n"u8.ToArray();
        var sepIndex = FindSequence(raw, separator);
        var actualBody = raw[(sepIndex + 4)..].ToArray();

        Assert.Equal(bodyBytes, actualBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_set_content_length_to_zero_when_post_with_empty_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent([])
        };

        var (headerLines, body) = ParseRaw(request);

        // POST with an empty body must emit Content-Length: 0 so that HTTP/1.0 servers
        // do not wait for body data until connection-close (RFC 1945 §7.2).
        var cl = headerLines.Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Content-Length: 0", cl);
        Assert.Empty(body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_match_content_length_to_body_size_when_post_with_large_body()
    {
        var largeBody = new byte[4096];
        new Random(42).NextBytes(largeBody);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(largeBody)
        };

        var buffer = MakeBuffer(16384);
        var written = Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer[..written]);

        var headerSection = raw[..raw.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
        var contentLengthLine = headerSection.Split("\r\n")
            .Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));

        var reportedLength = int.Parse(contentLengthLine.Split(": ")[1]);
        Assert.Equal(4096, reportedLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_place_body_after_header_separator()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_set_content_length_when_post_has_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent("Hello!", Encoding.ASCII)
        };

        var (headerLines, _) = ParseRaw(request);

        var cl = headerLines.Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Content-Length: 6", cl);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_omit_content_length_when_get_has_no_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines,
            h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_encode_binary_body_verbatim()
    {
        var bodyBytes = new byte[] { 0x00, 0x01, 0xFF, 0xFE, 0x7F, 0x80 };
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(bodyBytes)
        };

        var buffer = MakeBuffer();
        var written = Encoder.Encode(request, ref buffer);
        var raw = buffer[..written];

        var separator = "\r\n\r\n"u8.ToArray();
        var sepIndex = FindSequence(raw, separator);
        var actualBody = raw[(sepIndex + 4)..].ToArray();

        Assert.Equal(bodyBytes, actualBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_encode_utf8_json_body_correctly()
    {
        const string json = "{\"name\":\"value\",\"count\":42}";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var buffer = MakeBuffer();
        var written = Encoder.Encode(request, ref buffer);
        var raw = buffer[..written];

        var separator = "\r\n\r\n"u8.ToArray();
        var sepIndex = FindSequence(raw, separator);
        var actualBody = Encoding.UTF8.GetString(raw[(sepIndex + 4)..]);

        Assert.Equal(json, actualBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_not_truncate_body_when_body_contains_null_bytes()
    {
        var bodyBytes = "A\0B\0\0C"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(bodyBytes)
        };

        var buffer = MakeBuffer();
        var written = Encoder.Encode(request, ref buffer);
        var raw = buffer[..written];

        var separator = "\r\n\r\n"u8.ToArray();
        var sepIndex = FindSequence(raw, separator);
        var actualBody = raw[(sepIndex + 4)..].ToArray();

        Assert.Equal(bodyBytes.Length, actualBody.Length);
        Assert.Equal(bodyBytes, actualBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_encode_with_correct_content_length_when_body_is_2mb()
    {
        var bodyBytes = new byte[2 * 1024 * 1024];
        new Random(42).NextBytes(bodyBytes);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(bodyBytes)
        };

        var buffer = MakeBuffer(3 * 1024 * 1024);
        var written = Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer[..written]);

        var headerSection = raw[..raw.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
        var clLine = headerSection.Split("\r\n")
            .Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var reportedLength = int.Parse(clLine.Split(": ")[1]);

        Assert.Equal(2 * 1024 * 1024, reportedLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_separate_headers_from_body()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void Http10EncoderBody_should_encode_streaming_content_without_deadlock()
    {
        const string bodyText = "streaming body content";
        var bodyBytes = Encoding.ASCII.GetBytes(bodyText);

        // Use StreamContent to simulate a streaming (non-buffered) HttpContent
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StreamContent(new MemoryStream(bodyBytes))
        };

        var buffer = MakeBuffer();
        var written = Encoder.Encode(request, ref buffer);
        var raw = buffer[..written];

        var separator = "\r\n\r\n"u8.ToArray();
        var sepIndex = FindSequence(raw, separator);
        var actualBody = Encoding.ASCII.GetString(raw[(sepIndex + 4)..]);

        Assert.Equal(bodyText, actualBody);
    }
}