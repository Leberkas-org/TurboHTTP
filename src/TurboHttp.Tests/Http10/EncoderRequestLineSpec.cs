using System.Text;
using TurboHttp.Protocol.Http10;

namespace TurboHttp.Tests.Http10;

/// <summary>
/// Tests HTTP/1.0 request-line serialization per RFC 1945 §5.1.
/// Verifies that method, request URI, and HTTP-version are correctly encoded.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Encoder"/>.
/// RFC 1945 §5.1: Request-Line — Method SP Request-URI SP HTTP-Version CRLF.
/// </remarks>
public sealed class Http10EncoderRequestLineSpec
{
    private static Span<byte> MakeBuffer(int size = 8192) => new byte[size];

    private static string Encode(HttpRequestMessage request, int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Http10Encoder.Encode(request, ref buffer);
        return Encoding.ASCII.GetString(buffer[..written]);
    }

    private static (string requestLine, string[] headerLines, byte[] body) ParseRaw(HttpRequestMessage request,
        int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer[..written]);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerSection = raw[..separatorIndex];
        var bodyString = raw[(separatorIndex + 4)..];

        var lines = headerSection.Split("\r\n");
        var requestLine = lines[0];
        var headerLines = lines[1..];

        return (requestLine, headerLines, Encoding.ASCII.GetBytes(bodyString));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_contain_one_space_between_parts()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (requestLine, _, _) = ParseRaw(request);

        var parts = requestLine.Split(' ');
        Assert.Equal(3, parts.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_use_http10_protocol()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.EndsWith("HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_end_with_crlf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var raw = Encode(request);

        Assert.StartsWith("GET / HTTP/1.0\r\n", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_include_query_in_uri()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?q=hello&page=2");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /search?q=hello&page=2 HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_use_forward_slash_when_path_is_root()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET / HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_preserve_deep_path()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a/b/c/d");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /a/b/c/d HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_use_http10_version()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /path HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_preserve_path_and_query()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/data?key=val&x=1");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /api/data?key=val&x=1 HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_throw_argument_exception_when_method_is_lowercase()
    {
        var request = new HttpRequestMessage(new HttpMethod("get"), "http://example.com/");
        var threw = false;
        try
        {
            Span<byte> buffer = new byte[8192];
            Http10Encoder.Encode(request, ref buffer);
        }
        catch (ArgumentException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_encode_absolute_uri_when_absolute_form_requested()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path?q=1");
        var buffer = MakeBuffer();
        var written = Http10Encoder.Encode(request, ref buffer, absoluteForm: true);
        var raw = Encoding.ASCII.GetString(buffer[..written]);

        Assert.StartsWith("GET http://example.com/path?q=1 HTTP/1.0\r\n", raw);
    }

    [Theory(Timeout = 5000)]
    [InlineData("GET", "/path")]
    [InlineData("POST", "/submit")]
    [InlineData("PUT", "/res")]
    [InlineData("DELETE", "/res")]
    [InlineData("PATCH", "/res")]
    [InlineData("HEAD", "/resource")]
    [InlineData("OPTIONS", "/res")]
    [InlineData("TRACE", "/res")]
    public void Http10EncoderRequestLine_should_produce_correct_request_line_when_using_http_method(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), $"http://example.com{path}");
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent("body");
        }

        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal($"{method} {path} HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_normalize_to_slash_when_path_is_missing()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET / HTTP/1.0", requestLine);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_preserve_query_string()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/?a=1&b=2&c=3");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Contains("?a=1&b=2&c=3", requestLine);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_not_double_encode_when_path_contains_percent_encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri("http://example.com/path%20with%20spaces"));
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Contains("%20", requestLine);
        Assert.DoesNotContain("%2520", requestLine);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Http10EncoderRequestLine_should_strip_fragment_when_uri_contains_fragment()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page#section");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.DoesNotContain("#", requestLine);
        Assert.DoesNotContain("section", requestLine);
    }
}
