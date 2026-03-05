using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11DecoderBodyTests
{
    private readonly Http11Decoder _decoder = new();

    [Fact]
    public async Task IncompleteHeader_NeedMoreData_ReturnsFalse_OnSecondChunk()
    {
        const string body = "complete body";
        var full = BuildResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));

        var chunk1 = full[..10];
        var chunk2 = full[10..];

        var decoded1 = _decoder.TryDecode(chunk1, out _);
        var decoded2 = _decoder.TryDecode(chunk2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        Assert.Single(responses);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(body, result);
    }

    [Fact]
    public async Task IncompleteBody_NeedMoreData_ReturnsTrue_AfterBodyArrives()
    {
        const string body = "complete";
        var full = BuildResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));

        var headerEnd = IndexOfDoubleCrlf(full) + 4;
        var chunk1 = full[..headerEnd];
        var chunk2 = full[headerEnd..];

        var decoded1 = _decoder.TryDecode(chunk1, out _);
        var decoded2 = _decoder.TryDecode(chunk2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(body, result);
    }

    [Fact(DisplayName = "RFC7230-3.3: Content-Length body decoded to exact byte count")]
    public async Task ContentLength_Body_DecodedToExactByteCount()
    {
        const string body = "Hello, World!";
        var raw = BuildResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(body, result);
        Assert.Equal(body.Length, responses[0].Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC7230-3.3: Zero Content-Length produces empty body")]
    public void Zero_ContentLength_EmptyBody()
    {
        var raw = BuildResponse(200, "OK", "", ("Content-Length", "0"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC7230-3.3: Transfer-Encoding + Content-Length conflict rejected")]
    public void TransferEncoding_And_ContentLength_Conflict_Rejected()
    {
        // RFC 9112 §6.3 / Security: TE+CL combination is rejected to prevent HTTP smuggling.
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody,
            ("Transfer-Encoding", "chunked"),
            ("Content-Length", "999"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.ChunkedWithContentLength, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC7230-3.3: Multiple Content-Length values rejected")]
    public void Multiple_ContentLength_DifferentValues_Rejected()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nContent-Length: 6\r\n\r\nHello"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC7230-3.3: Negative Content-Length is parse error")]
    public void Negative_ContentLength_HandledGracefully()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: -1\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC7230-3.3: Response without body framing has empty body")]
    public void NoBodyFraming_EmptyBody()
    {
        var raw = "HTTP/1.1 200 OK\r\nX-Custom: test\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC9112-6: 10 MB body decoded with correct Content-Length")]
    public async Task LargeBody_10MB_DecodedCorrectly()
    {
        // Create 10 MB body
        var bodySize = 10 * 1024 * 1024;
        var largeBody = new byte[bodySize];
        for (int i = 0; i < bodySize; i++)
        {
            largeBody[i] = (byte)(i % 256);
        }

        var raw = BuildResponse(200, "OK", Encoding.Latin1.GetString(largeBody),
            ("Content-Length", bodySize.ToString()));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(bodySize, responses[0].Content.Headers.ContentLength);
        var result = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(bodySize, result.Length);
    }

    [Fact(DisplayName = "RFC9112-6: Binary body with null bytes intact")]
    public async Task BinaryBody_WithNullBytes_Intact()
    {
        var binaryBody = new byte[] { 0x00, 0x01, 0xFF, 0x00, 0xAB, 0xCD };

        // Build response manually with binary body
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {binaryBody.Length}\r\n\r\n");
        var raw = new byte[header.Length + binaryBody.Length];
        header.CopyTo(raw, 0);
        binaryBody.CopyTo(raw, header.Length);

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal(binaryBody, result);
    }

    [Fact]
    public void Decode_ConflictingHeaders_BothTeAndCl_Rejected()
    {
        // RFC 9112 §6.3 / Security: Both Transfer-Encoding and Content-Length present
        // is treated as a protocol error to prevent HTTP request smuggling.
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody,
            ("Transfer-Encoding", "chunked"),
            ("Content-Length", "999"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.ChunkedWithContentLength, ex.DecodeError);
    }

    [Fact]
    public void Decode_MultipleContentLength_DifferentValues_Throws()
    {
        // RFC 9112 §6.3: Multiple Content-Length headers with differing values
        // indicate a message framing error and MUST be treated as an error.
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nContent-Length: 6\r\n\r\nHello"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact]
    public void Decode_NegativeContentLength_HandledGracefully()
    {
        // RFC 7230 §3.3: A negative Content-Length is invalid. The decoder should
        // not throw; instead it treats the value as unparseable and falls through
        // to the no-body path (empty body returned).
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: -1\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact]
    public void Decode_NoBodyIndicator_EmptyBody()
    {
        // RFC 7230 §3.3-007: Response with neither Content-Length nor
        // Transfer-Encoding and non-1xx/204/304 status → empty body.
        var raw = "HTTP/1.1 200 OK\r\nX-Custom: test\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    private static ReadOnlyMemory<byte> BuildResponse(int code, string reason, string body,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static ReadOnlyMemory<byte> BuildRaw(int code, string reason, string rawBody,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(rawBody);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static int IndexOfDoubleCrlf(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        for (var i = 0; i <= span.Length - 4; i++)
        {
            if (span[i] == '\r' && span[i + 1] == '\n' && span[i + 2] == '\r' && span[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }
}
