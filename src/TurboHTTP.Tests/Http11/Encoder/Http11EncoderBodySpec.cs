using System.Buffers;
using System.Text;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Tests.Http11.Encoder;

/// <summary>
/// Tests message body and Content-Length encoding per RFC 9112 §6.
/// Verifies Content-Length presence, chunked framing, and bodyless request handling.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Encoder"/>.
/// RFC 9112 §6: Message body — determined by Content-Length or Transfer-Encoding.
/// </remarks>
public sealed class Http11EncoderBodySpec
{
    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_omit_content_length_when_bodyless_get()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.DoesNotContain("Content-Length:", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_set_content_length_when_post_with_body()
    {
        var content = new StringContent("test data");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        Assert.Contains("Content-Length:", result);
    }

    [Theory]
    [Trait("RFC", "RFC9112-6")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public void Http11Encoder_should_set_content_length_when_method_with_body(string method)
    {
        var content = new ByteArrayContent([1, 2, 3, 4, 5]);
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        Assert.Contains("Content-Length: 5\r\n", result);
    }

    [Theory]
    [Trait("RFC", "RFC9112-6")]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("DELETE")]
    public void Http11Encoder_should_omit_content_length_when_method_without_body(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/");
        var result = Encode(request);
        Assert.DoesNotContain("Content-Length:", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_separate_headers_from_body_when_empty_line()
    {
        var content = new StringContent("body content");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        var separatorIdx = result.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIdx > 0, "Empty line separator not found");
        Assert.StartsWith("body content", result[(separatorIdx + 4)..]);
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_preserve_binary_body_when_null_bytes()
    {
        var binaryData = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x00, 0x03 };
        var content = new ByteArrayContent(binaryData);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span);
        var bytes = buffer.Span[..written].ToArray();

        // Find body start (after \r\n\r\n)
        var bodyStart = -1;
        for (var i = 0; i < bytes.Length - 3; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
            {
                bodyStart = i + 4;
                break;
            }
        }

        Assert.True(bodyStart > 0);
        var body = bytes[bodyStart..(bodyStart + binaryData.Length)];
        Assert.Equal(binaryData, body);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Encoder_should_encode_chunked_when_transfer_encoding_chunked()
    {
        var content = new StringContent("Hello World");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = content
        };
        request.Headers.TransferEncodingChunked = true;

        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span);
        var bytes = buffer.Span[..written].ToArray();
        var result = Encoding.ASCII.GetString(bytes);

        // Verify Transfer-Encoding: chunked is present
        Assert.Contains("Transfer-Encoding: chunked\r\n", result);

        // Find body start (after \r\n\r\n)
        var separatorIdx = result.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIdx > 0);
        var bodyPart = result[(separatorIdx + 4)..];

        // Verify chunked encoding format: size in hex + CRLF + data + CRLF
        // "Hello World" = 11 bytes = 0xb in hex
        Assert.StartsWith("b\r\nHello World\r\n", bodyPart);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Encoder_should_terminate_with_zero_chunk_when_chunked_body()
    {
        var content = new StringContent("Test");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        request.Headers.TransferEncodingChunked = true;

        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span);
        var bytes = buffer.Span[..written].ToArray();
        var result = Encoding.ASCII.GetString(bytes);

        // Verify the message ends with the final chunk: 0\r\n\r\n
        Assert.EndsWith("0\r\n\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-7")]
    public void Http11Encoder_should_omit_content_length_when_chunked_transfer_encoding()
    {
        var content = new StringContent("Some data here");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        request.Headers.TransferEncodingChunked = true;

        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span);
        var bytes = buffer.Span[..written].ToArray();
        var result = Encoding.ASCII.GetString(bytes);

        // RFC 7230 Section 3.3.2: Content-Length MUST NOT be sent when Transfer-Encoding is present
        Assert.DoesNotContain("Content-Length:", result);
        Assert.Contains("Transfer-Encoding: chunked\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_end_with_blank_line_when_get_request()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.EndsWith("\r\n\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_set_content_type_and_length_when_post_json_body()
    {
        const string json = """{"name":"test"}""";
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/users")
        {
            Content = content
        };
        var result = Encode(request);

        Assert.Contains("POST /users HTTP/1.1\r\n", result);
        Assert.Contains("Content-Type: application/json", result);
        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(json)}", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_place_body_after_blank_line_when_post_json_body()
    {
        const string json = """{"x":1}""";
        var content = new StringContent(json);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/data")
        {
            Content = content
        };
        var result = Encode(request);

        var separatorIdx = result.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIdx > 0);
        Assert.Equal(json, result[(separatorIdx + 4)..]);
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_throw_when_buffer_too_small_for_body()
    {
        var content = new ByteArrayContent(new byte[3000]);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/data")
        {
            Content = content
        };
        var buffer = new Memory<byte>(new byte[200]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Http11Encoder.Encode(request, ref span);
        });
    }

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Encoder_should_throw_when_buffer_too_small_for_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var buffer = new Memory<byte>(new byte[1]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Http11Encoder.Encode(request, ref span);
        });
    }

    private static string Encode(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}
