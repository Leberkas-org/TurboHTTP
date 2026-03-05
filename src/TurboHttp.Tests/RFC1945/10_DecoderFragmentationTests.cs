using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

public sealed class Http10DecoderFragmentationTests
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

    [Fact(DisplayName = "RFC1945-FRAG-001: Headers split across two chunks reassembled")]
    public void Fragmentation_HeadersSplitAcrossTwoChunks_ReassembledCorrectly()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nHello");

        var chunk1 = full[..15];
        var chunk2 = full[15..];

        var result1 = decoder.TryDecode(chunk1, out var r1);
        Assert.False(result1);
        Assert.Null(r1);

        var result2 = decoder.TryDecode(chunk2, out var r2);
        Assert.True(result2);
        Assert.NotNull(r2);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-FRAG-002: Body split across two chunks reassembled")]
    public async Task Fragmentation_BodySplitAcrossTwoChunks_ReassembledCorrectly()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 10\r\n\r\n1234567890");

        var separatorIdx = FindSequence(full.Span, "\r\n\r\n"u8) + 4;
        var chunk1 = full[..(separatorIdx + 5)];
        var chunk2 = full[(separatorIdx + 5)..];

        var result1 = decoder.TryDecode(chunk1, out _);
        Assert.False(result1);

        var result2 = decoder.TryDecode(chunk2, out var response);
        Assert.True(result2);
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("1234567890", body);
    }

    [Fact(DisplayName = "RFC1945-FRAG-003: Single byte chunks eventually decoded")]
    public void Fragmentation_SingleByteChunks_EventuallyDecodes()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC").ToArray();

        HttpResponseMessage? response = null;
        var decoded = false;

        for (var i = 0; i < full.Length; i++)
        {
            var chunk = new ReadOnlyMemory<byte>(full, i, 1);
            if (decoder.TryDecode(chunk, out response))
            {
                decoded = true;
                break;
            }
        }

        Assert.True(decoded);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-FRAG-004: Multiple responses decoded independently")]
    public void Fragmentation_MultipleResponses_DecodedIndependently()
    {
        var decoder = new Http10Decoder();

        var resp1 = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nONE");
        var resp2 = Bytes("HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\n\r\n");

        decoder.TryDecode(resp1, out var r1);
        decoder.TryDecode(resp2, out var r2);

        Assert.Equal(HttpStatusCode.OK, r1!.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, r2!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-FRAG-005: Incomplete header returns false and buffers")]
    public void Fragmentation_IncompleteHeader_ReturnsFalseAndBuffers()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200 OK\r\nContent-Le");

        var result = decoder.TryDecode(incomplete, out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(DisplayName = "RFC1945-FRAG-006: Incomplete body returns false and buffers")]
    public void Fragmentation_IncompleteBody_ReturnsFalseAndBuffers()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nonly10bytes");

        var result = decoder.TryDecode(incomplete, out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact(DisplayName = "RFC1945-FRAG-007: Three chunks decoded correctly")]
    public async Task Fragmentation_ThreeChunks_DecodesCorrectly()
    {
        var decoder = new Http10Decoder();
        const string full = "HTTP/1.0 200 OK\r\nContent-Length: 9\r\n\r\nABCDEFGHI";
        var bytes = Bytes(full).ToArray();

        var third = bytes.Length / 3;
        var c1 = new ReadOnlyMemory<byte>(bytes, 0, third);
        var c2 = new ReadOnlyMemory<byte>(bytes, third, third);
        var c3 = new ReadOnlyMemory<byte>(bytes, third * 2, bytes.Length - third * 2);

        Assert.False(decoder.TryDecode(c1, out _));
        Assert.False(decoder.TryDecode(c2, out _));
        var result = decoder.TryDecode(c3, out var response);

        Assert.True(result);
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("ABCDEFGHI", body);
    }

    [Fact(DisplayName = "RFC1945-FRAG-008: Status-line split at byte 1 reassembled")]
    public void Should_Reassemble_When_StatusLineSplitAtByte1()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC");

        Assert.False(decoder.TryDecode(full[..1], out _));
        Assert.True(decoder.TryDecode(full[1..], out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-FRAG-009: Status-line split inside version reassembled")]
    public void Should_Reassemble_When_StatusLineSplitInsideVersion()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC");

        // Split inside "HTTP/" — at offset 5
        Assert.False(decoder.TryDecode(full[..5], out _));
        Assert.True(decoder.TryDecode(full[5..], out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-FRAG-010: Header name split across two reads")]
    public void Should_Reassemble_When_HeaderNameSplit()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC");

        // Split inside "Content-" (offset ~25)
        Assert.False(decoder.TryDecode(full[..25], out _));
        Assert.True(decoder.TryDecode(full[25..], out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-FRAG-011: Header value split across two reads")]
    public void Should_Reassemble_When_HeaderValueSplit()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC");

        // Split inside header value area (offset ~33, inside "3\r\n")
        Assert.False(decoder.TryDecode(full[..33], out _));
        Assert.True(decoder.TryDecode(full[33..], out var response));
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-FRAG-012: Body split mid-content reassembled")]
    public async Task Should_Reassemble_When_BodySplitMidContent()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 10\r\n\r\n0123456789");

        var separatorIdx = FindSequence(full.Span, "\r\n\r\n"u8) + 4;
        // Split body in the middle
        var splitPoint = separatorIdx + 5;

        Assert.False(decoder.TryDecode(full[..splitPoint], out _));
        Assert.True(decoder.TryDecode(full[splitPoint..], out var response));
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("0123456789", body);
    }
}
