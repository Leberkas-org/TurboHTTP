using System.Buffers;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11EncoderBodyTests
{
    [Fact(DisplayName = "RFC7230-3.3: No Content-Length for bodyless GET")]
    public void Test_No_Content_Length_For_GET()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.DoesNotContain("Content-Length:", result);
    }

    [Fact(DisplayName = "RFC7230-3.3: Content-Length set for POST body")]
    public void Test_Content_Length_For_POST()
    {
        var content = new StringContent("test data");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        Assert.Contains("Content-Length:", result);
    }

    [Theory(DisplayName = "RFC9112-6: {method} with body gets Content-Length [{method}]")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public void Test_Methods_With_Body_Get_Content_Length(string method)
    {
        var content = new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5 });
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        Assert.Contains("Content-Length: 5\r\n", result);
    }

    [Theory(DisplayName = "RFC9112-6: {method} without body omits Content-Length [{method}]")]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("DELETE")]
    public void Test_Methods_Without_Body_Omit_Content_Length(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/");
        var result = Encode(request);
        Assert.DoesNotContain("Content-Length:", result);
    }

    [Fact(DisplayName = "RFC9112-6: Empty line separates headers from body")]
    public void Test_Empty_Line_Separator()
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

    [Fact(DisplayName = "RFC9112-6: Binary body with null bytes preserved")]
    public void Test_Binary_Body_Preserved()
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

    [Fact(DisplayName = "RFC7230-4.1: Chunked Transfer-Encoding for streamed body")]
    public void Test_Chunked_Transfer_Encoding()
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

    [Fact(DisplayName = "RFC7230-4.1: Chunked body terminated with final 0-chunk")]
    public void Test_Chunked_Body_Terminator()
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

    [Fact(DisplayName = "RFC7230-3.3.2: Content-Length absent when Transfer-Encoding is chunked")]
    public void Test_No_Content_Length_When_Chunked()
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
    public void Get_EndsWithBlankLine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.EndsWith("\r\n\r\n", result);
    }

    [Fact]
    public void Post_WithJsonBody_SetsContentTypeAndLength()
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
    public void Post_WithJsonBody_BodyAppearsAfterBlankLine()
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
    public void Post_BufferTooSmallForBody_Throws()
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
    public void Encode_BufferTooSmallForHeaders_Throws()
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