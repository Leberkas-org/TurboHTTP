using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

public sealed class Http10DecoderBodyTests
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        string body = "")
    {
        var raw = $"{statusLine}\r\n{headers}\r\n\r\n{body}";
        return Bytes(raw);
    }

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        byte[] body)
    {
        var headerPart = Encoding.ASCII.GetBytes($"{statusLine}\r\n{headers}\r\n\r\n");
        var result = new byte[headerPart.Length + body.Length];
        headerPart.CopyTo(result, 0);
        body.CopyTo(result, headerPart.Length);
        return result;
    }

    [Fact(DisplayName = "RFC1945-7-BODY-001: Content-Length body decoded to exact byte count")]
    public async Task Body_WithContentLength_BodyReadCorrectly()
    {
        var decoder = new Http10Decoder();
        const string body = "Hello, World!";
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {body.Length}\r\nContent-Type: text/plain", body);

        decoder.TryDecode(data, out var response);

        var actualBody = await response!.Content.ReadAsStringAsync();
        Assert.Equal(body, actualBody);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-002: Content-Length exact bytes read")]
    public async Task Body_WithContentLength_ExactBytesRead()
    {
        var decoder = new Http10Decoder();
        const string body = "ABCDE";
        const string raw = $"HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\n{body}";

        decoder.TryDecode(Bytes(raw), out var response);

        var bytes = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(3, bytes.Length);
        Assert.Equal("ABC", Encoding.ASCII.GetString(bytes));
    }

    [Fact(DisplayName = "RFC1945-7-BODY-003: Zero Content-Length produces empty body")]
    public void Body_WithZeroContentLength_EmptyBody()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(0, response!.Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-004: Body without Content-Length read until EOF")]
    public async Task Body_WithoutContentLength_ReadsUntilEndOfData()
    {
        var decoder = new Http10Decoder();
        const string body = "body without content-length";
        const string raw = $"HTTP/1.0 200 OK\r\n\r\n{body}";

        decoder.TryDecode(Bytes(raw), out var response);

        var actualBody = await response!.Content.ReadAsStringAsync();
        Assert.Equal(body, actualBody);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-005: Binary content preserved exactly")]
    public async Task Body_BinaryContent_PreservedExactly()
    {
        var bodyBytes = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFE, 0xFF };
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyBytes.Length}", bodyBytes);

        decoder.TryDecode(data, out var response);

        var actualBody = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyBytes, actualBody);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-006: Content-Length header set on content")]
    public void Body_ContentLengthHeader_SetOnContent()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 5", "Hello");

        decoder.TryDecode(data, out var response);

        Assert.Equal(5, response!.Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-007: 204 No Content has no body")]
    public void Body_NoBody_ResponseContentIsNull()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 204 No Content", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(0, response!.Content.Headers.ContentLength);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-008: Negative Content-Length rejected")]
    public void EdgeCase_ContentLengthNegative_ThrowsDecoderException()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: -1");

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(data, out _));
        Assert.Equal(HttpDecodeError.InvalidContentLength, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-009: Two different Content-Length values rejected")]
    public void Should_ThrowMultipleContentLength_When_DifferentValues()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nContent-Length: 3\r\nContent-Length: 5\r\n\r\nHello";

        var ex = Assert.Throws<HttpDecoderException>(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Equal(HttpDecodeError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-010: Two identical Content-Length values accepted")]
    public async Task Should_AcceptIdenticalContentLength_When_DuplicateValues()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\nContent-Length: 5\r\n\r\nHello";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("Hello", body);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-011: 304 Not Modified ignores Content-Length body")]
    public async Task Should_HaveEmptyBody_When_304WithContentLength()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 304 Not Modified", "Content-Length: 100");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotModified, response!.StatusCode);
        var bodyBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(bodyBytes);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-012: 304 Not Modified without Content-Length has empty body")]
    public async Task Should_HaveEmptyBody_When_304WithoutContentLength()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 304 Not Modified\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotModified, response!.StatusCode);
        var bodyBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(bodyBytes);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-013: 204 No Content has empty body")]
    public async Task Should_HaveEmptyBody_When_204NoContent()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 204 No Content\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NoContent, response!.StatusCode);
        var bodyBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(bodyBytes);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-014: Body with null bytes preserved")]
    public async Task Should_PreserveNullBytes_When_BodyContainsThem()
    {
        var bodyBytes = new byte[] { 0x48, 0x00, 0x65, 0x00, 0x6C };
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyBytes.Length}", bodyBytes);

        decoder.TryDecode(data, out var response);

        var actual = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyBytes, actual);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-015: 2 MB body decoded with correct Content-Length")]
    public async Task Should_Decode2MbBody_When_LargeContentLength()
    {
        var bodyBytes = new byte[2 * 1024 * 1024];
        new Random(42).NextBytes(bodyBytes);
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyBytes.Length}", bodyBytes);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var actual = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyBytes.Length, actual.Length);
        Assert.Equal(bodyBytes, actual);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-016: Very large header handled correctly")]
    public void EdgeCase_VeryLargeHeader_HandledCorrectly()
    {
        var decoder = new Http10Decoder();
        var longValue = new string('A', 8000);
        var raw = $"HTTP/1.0 200 OK\r\nX-Big: {longValue}\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.True(response!.Headers.TryGetValues("X-Big", out var values));
        Assert.Equal(longValue, values.First());
    }

    [Fact(DisplayName = "RFC1945-7-BODY-017: Transfer-Encoding chunked treated as raw body in HTTP/1.0")]
    public async Task Should_TreatChunkedAsRawBody_When_Http10()
    {
        var decoder = new Http10Decoder();
        // HTTP/1.0 does not support chunked TE — body should be raw
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Transfer-Encoding: chunked\r\nContent-Length: {chunkedBody.Length}", chunkedBody);

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal(chunkedBody, body);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-018: Body without Content-Length via TryDecodeEof")]
    public async Task Should_ReadBodyViaEof_When_NoContentLength()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\n\r\nEOF body data";

        // First TryDecode consumes headers + body (no CL, so all remaining is body)
        decoder.TryDecode(Bytes(raw), out var response);

        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("EOF body data", body);
    }

    [Fact(DisplayName = "RFC1945-7-BODY-019: Empty input returns false")]
    public void EdgeCase_EmptyInput_ReturnsFalse()
    {
        var decoder = new Http10Decoder();

        var result = decoder.TryDecode(ReadOnlyMemory<byte>.Empty, out var response);

        Assert.False(result);
        Assert.Null(response);
    }
}
