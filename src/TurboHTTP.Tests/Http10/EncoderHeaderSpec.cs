using System.Net.Http.Headers;
using System.Text;
using Encoder = TurboHTTP.Protocol.Http10.Encoder;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10EncoderHeaderSpec
{
    private static Span<byte> MakeBuffer(int size = 8192) => new byte[size];

    private static string[] ParseRaw(HttpRequestMessage request, int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer[..written]);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerSection = raw[..separatorIndex];

        var lines = headerSection.Split("\r\n");
        var headerLines = lines[1..];

        return headerLines;
    }

    private static string Encode(HttpRequestMessage request, int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Encoder.Encode(request, ref buffer);
        return Encoding.ASCII.GetString(buffer[..written]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_remove_host_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var headerLines = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_remove_transfer_encoding_header()
    {
        // Transfer-Encoding ist HTTP/1.1 (RFC 2616 §14.41)
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked");

        var headerLines = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_preserve_custom_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Custom-Header", "my-value");

        var headerLines = ParseRaw(request);

        Assert.Contains(headerLines, h => h == "X-Custom-Header: my-value");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_preserve_all_custom_headers_when_multiple_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Header-A", "value-a");
        request.Headers.TryAddWithoutValidation("X-Header-B", "value-b");

        var headerLines = ParseRaw(request);

        Assert.Contains(headerLines, h => h == "X-Header-A: value-a");
        Assert.Contains(headerLines, h => h == "X-Header-B: value-b");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_format_as_name_colon_space_value()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "test-value");

        var headerLines = ParseRaw(request);

        var header = headerLines.Single(h => h.StartsWith("X-Test:"));
        Assert.Equal("X-Test: test-value", header);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_end_each_header_with_crlf()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_emit_each_value_on_separate_line_when_multi_value_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var headerLines = ParseRaw(request);

        var acceptLines = headerLines.Where(h => h.StartsWith("Accept:", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(2, acceptLines.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_preserve_accept_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var headerLines = ParseRaw(request);

        Assert.Contains(headerLines, h => h.StartsWith("Accept:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_contain_only_mandatory_headers_when_no_custom_headers_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var headerLines = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(headerLines, h => h.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_use_double_crlf_separator()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var raw = Encode(request);

        Assert.Contains("\r\n\r\n", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_omit_host_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var headerLines = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_terminate_every_header_with_crlf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-One", "1");
        request.Headers.TryAddWithoutValidation("X-Two", "2");

        var raw = Encode(request);
        var headerSection = raw[..raw.IndexOf("\r\n\r\n", StringComparison.Ordinal)];

        Assert.DoesNotContain("\n", headerSection.Replace("\r\n", ""));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_preserve_header_name_casing()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-My-Custom-Header", "value");

        var headerLines = ParseRaw(request);

        Assert.Contains(headerLines, h => h.StartsWith("X-My-Custom-Header:"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_emit_all_custom_headers_when_multiple_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-First", "a");
        request.Headers.TryAddWithoutValidation("X-Second", "b");
        request.Headers.TryAddWithoutValidation("X-Third", "c");

        var headerLines = ParseRaw(request);

        Assert.Contains(headerLines, h => h == "X-First: a");
        Assert.Contains(headerLines, h => h == "X-Second: b");
        Assert.Contains(headerLines, h => h == "X-Third: c");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_preserve_semicolon_when_in_header_value()
    {
        var content = new ByteArrayContent("x"u8.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", "text/html; charset=utf-8");
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = content
        };

        var headerLines = ParseRaw(request);

        Assert.Contains(headerLines,
            h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)
                 && h.Contains(";"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.2")]
    public void Http10EncoderHeader_should_throw_argument_exception_when_header_value_contains_nul()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Bad", "value\0evil");

        var threw = false;
        try
        {
            Span<byte> buffer = new byte[8192];
            Encoder.Encode(request, ref buffer);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Assert.True(threw);
    }
}