using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

public sealed class Http10DecoderStateTests
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

    [Fact(DisplayName = "RFC1945-STATE-001: TryDecodeEof with buffered data returns true")]
    public void TryDecodeEof_WithBufferedData_ReturnsTrue()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200 OK\r\n\r\nsome body data");
        decoder.TryDecode(incomplete, out _);

        var decoder2 = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nshort");
        decoder2.TryDecode(partial, out _);

        var result = decoder2.TryDecodeEof(out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact(DisplayName = "RFC1945-STATE-002: TryDecodeEof with empty buffer returns false")]
    public void TryDecodeEof_WithEmptyBuffer_ReturnsFalse()
    {
        var decoder = new Http10Decoder();

        var result = decoder.TryDecodeEof(out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(DisplayName = "RFC1945-STATE-003: TryDecodeEof with incomplete header returns false")]
    public void TryDecodeEof_WithIncompleteHeader_ReturnsFalse()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200");
        decoder.TryDecode(incomplete, out _);

        var result = decoder.TryDecodeEof(out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(DisplayName = "RFC1945-STATE-004: TryDecodeEof clears remainder")]
    public void TryDecodeEof_ClearsRemainder()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nshort");
        decoder.TryDecode(partial, out _);

        decoder.TryDecodeEof(out _);

        var result = decoder.TryDecodeEof(out var response);
        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(DisplayName = "RFC1945-STATE-005: Reset clears buffered data")]
    public void Reset_ClearsBufferedData()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nincomplete");
        decoder.TryDecode(partial, out _);

        decoder.Reset();

        var result = decoder.TryDecodeEof(out var response);
        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(DisplayName = "RFC1945-STATE-006: Reset allows decoding new response")]
    public void Reset_AfterReset_DecodesNewResponseCorrectly()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nincomplete");
        decoder.TryDecode(partial, out _);

        decoder.Reset();

        var fresh = BuildRawResponse("HTTP/1.0 201 Created", "Content-Length: 0");
        var result = decoder.TryDecode(fresh, out var response);

        Assert.True(result);
        Assert.Equal(System.Net.HttpStatusCode.Created, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-STATE-007: Reset called multiple times does not throw")]
    public void Reset_CalledMultipleTimes_DoesNotThrow()
    {
        var decoder = new Http10Decoder();

        var ex = Record.Exception(() =>
        {
            decoder.Reset();
            decoder.Reset();
            decoder.Reset();
        });

        Assert.Null(ex);
    }

    [Fact(DisplayName = "RFC1945-STATE-008: Empty input returns false")]
    public void EdgeCase_EmptyInput_ReturnsFalse()
    {
        var decoder = new Http10Decoder();

        var result = decoder.TryDecode(ReadOnlyMemory<byte>.Empty, out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(DisplayName = "RFC1945-STATE-009: Decoder state preserved across partial decodes")]
    public void EdgeCase_StatePreservedAcrossPartials()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nX-Header: value\r\nContent-Length: 5\r\n\r\nHello");

        // First decode with partial data
        var chunk1 = full[..20];
        var result1 = decoder.TryDecode(chunk1, out _);
        Assert.False(result1);

        // Second decode with remaining data
        var chunk2 = full[20..];
        var result2 = decoder.TryDecode(chunk2, out var response);

        Assert.True(result2);
        Assert.NotNull(response);
        Assert.True(response.Headers.TryGetValues("X-Header", out var values));
        Assert.Contains("value", values);
    }

    [Fact(DisplayName = "RFC1945-STATE-010: Decoder reusable after successful decode")]
    public void EdgeCase_DecoderReusableAfterDecode()
    {
        var decoder = new Http10Decoder();

        var data1 = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        decoder.TryDecode(data1, out var response1);
        Assert.NotNull(response1);

        decoder.Reset();

        var data2 = BuildRawResponse("HTTP/1.0 404 Not Found", "Content-Length: 0");
        decoder.TryDecode(data2, out var response2);

        Assert.NotNull(response2);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response2.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-STATE-011: Multiple Reset calls idempotent")]
    public void EdgeCase_MultipleResetIdempotent()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200");
        decoder.TryDecode(partial, out _);

        decoder.Reset();
        decoder.Reset();

        // Should still be able to decode new data
        var fresh = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        var result = decoder.TryDecode(fresh, out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact(DisplayName = "RFC1945-STATE-012: Decoder maintains state through multiple fragments")]
    public void EdgeCase_StateMaintenanceMultipleFragments()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nHello");

        // Feed one byte at a time for first part, then flush
        for (var i = 0; i < full.Length - 1; i++)
        {
            var chunk = full.Slice(i, 1);
            var result = decoder.TryDecode(chunk, out var response);
            if (result)
            {
                Assert.NotNull(response);
                return;
            }
        }

        // Last chunk
        Assert.True(decoder.TryDecode(full.Slice(full.Length - 1, 1), out var finalResponse));
        Assert.NotNull(finalResponse);
    }

    [Fact(DisplayName = "RFC1945-STATE-013: TryDecodeEof called after successful decode returns false")]
    public void EdgeCase_TryDecodeEofAfterSuccess()
    {
        var decoder = new Http10Decoder();
        var complete = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(complete, out _);

        var result = decoder.TryDecodeEof(out var response);
        Assert.False(result);
        Assert.Null(response);
    }
}
