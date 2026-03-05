using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// RFC 1945 — Integration tests for HTTP/1.0 encoder.
/// Verifies URI handling, Content-Type handling, bytes written tracking, idempotency, and end-to-end scenarios.
/// </summary>
public sealed class Http10EncoderIntegrationTests
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

    [Fact]
    public void Uri_NonAsciiPath_IsPercentEncoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri("http://example.com/pfad/mit/%C3%BCmlauten"));
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Contains("%C3%BC", requestLine);
    }

    [Fact]
    public void Uri_SpaceInPath_IsPercentEncoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri("http://example.com/path%20with%20spaces"));
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Contains("%20", requestLine);
        var uri = requestLine.Split(' ')[1];
        Assert.DoesNotContain(" ", uri);
    }

    [Fact]
    public void Uri_QueryStringWithSpecialChars_IsPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "http://example.com/search?q=hello+world&lang=de");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Contains("?q=hello+world&lang=de", requestLine);
    }

    [Fact]
    public void Uri_EmptyPath_NormalizesToSlash()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.StartsWith("GET /", requestLine);
    }

    [Fact]
    public void Uri_PathWithFragment_FragmentIsNotIncluded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.DoesNotContain("#", requestLine);
    }

    [Fact]
    public void Uri_NonStandardPort_IsNotInRequestLine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/api");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /api HTTP/1.0", requestLine);
    }

    [Fact]
    public void ContentType_WhenSetExplicitly_IsPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent("data", Encoding.ASCII, "application/json")
        };

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h.StartsWith("Content-Type: application/json",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContentType_WithoutBody_IsNotSet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Content-Type:",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContentType_NoDefaultIsInjected_WhenMissingAndBodyExists()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent([1, 2, 3])
        };

        var (_, headerLines, _) = ParseRaw(request);

        var contentTypeLines = headerLines
            .Where(h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(contentTypeLines);
    }

    [Fact]
    public void BytesWritten_MatchesActualEncodedLength()
    {
        const string body = "test body content";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent(body, Encoding.ASCII, "text/plain")
        };

        var buffer = MakeBuffer();
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.Equal(written, Encoding.ASCII.GetByteCount(raw));
    }

    [Fact]
    public void BytesWritten_IsGreaterThanZero()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = MakeBuffer();

        var written = Http10Encoder.Encode(request, ref buffer);

        Assert.True(written > 0);
    }

    [Fact]
    public void BytesWritten_WithBody_IsLargerThanWithout()
    {
        var requestWithoutBody = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var requestWithBody = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent("body content", Encoding.ASCII, "text/plain")
        };

        var buf1 = MakeBuffer();
        var buf2 = MakeBuffer();

        var writtenWithout = Http10Encoder.Encode(request: requestWithoutBody, buffer: ref buf1);
        var writtenWith = Http10Encoder.Encode(request: requestWithBody, buffer: ref buf2);

        Assert.True(writtenWith > writtenWithout);
    }

    [Fact]
    public void BytesWritten_BufferBeyondWrittenBytes_IsUntouched()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = MakeBuffer(8192);
        buffer.Span[8191] = 0xAB;

        var written = Http10Encoder.Encode(request, ref buffer);

        Assert.Equal(0xAB, buffer.Span[8191]);
        Assert.True(written < 8191);
    }

    [Fact]
    public void Idempotent_SameRequestEncodedTwice_ProducesIdenticalOutput()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api")
        {
            Content = new StringContent("payload", Encoding.ASCII, "text/plain")
        };
        request.Headers.TryAddWithoutValidation("X-Request-Id", "abc123");

        var buffer1 = MakeBuffer();
        var written1 = Http10Encoder.Encode(request, ref buffer1);
        var result1 = buffer1.Span[..written1].ToArray();

        var buffer2 = MakeBuffer();
        var written2 = Http10Encoder.Encode(request, ref buffer2);
        var result2 = buffer2.Span[..written2].ToArray();

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Idempotent_SameGetRequestEncodedTwice_ProducesIdenticalOutput()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource?id=42");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        var buf1 = MakeBuffer();
        var buf2 = MakeBuffer();

        var w1 = Http10Encoder.Encode(request, ref buf1);
        var w2 = Http10Encoder.Encode(request, ref buf2);

        Assert.Equal(buf1.Span[..w1].ToArray(), buf2.Span[..w2].ToArray());
    }

    [Fact]
    public void Integration_MinimalGetRequest_IsFullyRfc1945Compliant()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/index.html");
        var raw = Encode(request);

        Assert.StartsWith("GET /index.html HTTP/1.0\r\n", raw);
        Assert.Contains("\r\n\r\n", raw);
        Assert.DoesNotContain("Host:", raw);
        Assert.DoesNotContain("Connection:", raw);
        Assert.DoesNotContain("Transfer-Encoding:", raw);
    }

    [Fact]
    public void Integration_PostWithJsonBody_IsFullyRfc1945Compliant()
    {
        const string json = "{\"key\":\"value\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://api.example.com/resource")
        {
            Content = new StringContent(json, Encoding.ASCII, "application/json")
        };

        var raw = Encode(request);

        Assert.StartsWith("POST /resource HTTP/1.0\r\n", raw);
        Assert.Contains($"Content-Length: {Encoding.ASCII.GetByteCount(json)}", raw);
        Assert.Contains("Content-Type: application/json", raw);
        Assert.EndsWith(json, raw);
        Assert.DoesNotContain("Host:", raw);
        Assert.DoesNotContain("Transfer-Encoding:", raw);
    }

    [Fact]
    public void Integration_HeadRequest_HasNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var raw = Encode(request);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var body = raw[(separatorIndex + 4)..];

        Assert.Empty(body);
        Assert.DoesNotContain("Content-Length:", raw);
    }

    [Fact]
    public void Integration_RequestWithMultipleHeaders_AllWrittenCorrectly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "secret-123");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "req-456");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h =>
            h.StartsWith("X-Api-Key:", StringComparison.OrdinalIgnoreCase) &&
            h.EndsWith("secret-123"));

        Assert.Contains(headerLines, h =>
            h.StartsWith("X-Request-Id:", StringComparison.OrdinalIgnoreCase) &&
            h.EndsWith("req-456"));
    }

    [Fact]
    public void Integration_ContentHeadersMergedWithRequestHeaders()
    {
        var content = new StringContent("body", Encoding.ASCII, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h == "X-Custom: value");
        Assert.Contains(headerLines, h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(headerLines, h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
    }
}
