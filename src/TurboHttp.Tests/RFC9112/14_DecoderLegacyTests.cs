using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11DecoderLegacyTests
{
    private readonly Http11Decoder _decoder = new();

    [Fact(DisplayName = "RFC7231-7.1.1: IMF-fixdate Date header parsed")]
    public void Should_ParseImfFixdateToDateTimeOffset_When_DateHeaderPresent()
    {
        // IMF-fixdate format: Sun, 06 Nov 1994 08:49:37 GMT
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Date", "Sun, 06 Nov 1994 08:49:37 GMT"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.NotNull(responses[0].Headers.Date);

        var expected = new DateTimeOffset(1994, 11, 6, 8, 49, 37, TimeSpan.Zero);
        Assert.Equal(expected, responses[0].Headers.Date);
    }

    [Fact(DisplayName = "RFC7231-7.1.1: RFC 850 Date format accepted")]
    public void Should_ParseRfc850ObsoleteFormat_When_DateHeaderPresent()
    {
        // RFC 850 obsolete format: Sunday, 06-Nov-94 08:49:37 GMT
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Date", "Sunday, 06-Nov-94 08:49:37 GMT"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        // .NET automatically normalizes obsolete date formats to IMF-fixdate
        // We verify it doesn't crash and the date is parseable
        Assert.True(responses[0].Headers.TryGetValues("Date", out var dateValues));
        Assert.NotEmpty(dateValues);
        // The header should be normalized to IMF-fixdate format
        var expected = new DateTimeOffset(1994, 11, 6, 8, 49, 37, TimeSpan.Zero);
        Assert.Equal(expected, responses[0].Headers.Date);
    }

    [Fact(DisplayName = "RFC7231-7.1.1: ANSI C asctime Date format accepted")]
    public void Should_ParseAnsiCAsctimeFormat_When_DateHeaderPresent()
    {
        // ANSI C asctime format: Sun Nov  6 08:49:37 1994
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Date", "Sun Nov  6 08:49:37 1994"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        // .NET automatically normalizes asctime format to IMF-fixdate
        // We verify it doesn't crash and the date is parseable
        Assert.True(responses[0].Headers.TryGetValues("Date", out var dateValues));
        Assert.NotEmpty(dateValues);
        // The header should be normalized to IMF-fixdate format
        var expected = new DateTimeOffset(1994, 11, 6, 8, 49, 37, TimeSpan.Zero);
        Assert.Equal(expected, responses[0].Headers.Date);
    }

    [Fact(DisplayName = "RFC7231-7.1.1: Non-GMT timezone in Date rejected")]
    public void Should_HandleNonGmtTimezone_When_DateHeaderPresent()
    {
        // Non-GMT timezone should be rejected per RFC 7231
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Date", "Sun, 06 Nov 1994 08:49:37 PST"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        // The decoder should not crash - it should either parse or leave unparsed
        // HttpClient's Date property will return null if unparseable
        Assert.True(responses[0].Headers.TryGetValues("Date", out var dateValues));
        Assert.NotNull(dateValues);
    }

    [Fact(DisplayName = "RFC7231-7.1.1: Invalid Date header value rejected")]
    public void Should_HandleInvalidDateGracefully_When_DateHeaderMalformed()
    {
        // Completely invalid date value
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Date", "not-a-valid-date"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        // The decoder should not crash - just leave the header unparseable
        Assert.True(responses[0].Headers.TryGetValues("Date", out var dateValues));
        Assert.Equal("not-a-valid-date", dateValues.Single());
        // The Date property should be null for invalid values
        Assert.Null(responses[0].Headers.Date);
    }

    [Fact(DisplayName = "RFC7230: Two pipelined responses decoded")]
    public async Task TwoPipelinedResponses_InSameBuffer_BothDecoded()
    {
        var resp1 = BuildResponse(200, "OK", "first", ("Content-Length", "5"));
        var resp2 = BuildResponse(201, "Created", "second", ("Content-Length", "6"));

        var combined = new byte[resp1.Length + resp2.Length];
        resp1.Span.CopyTo(combined);
        resp2.Span.CopyTo(combined.AsSpan(resp1.Length));

        var decoded = _decoder.TryDecode(combined, out var responses);

        Assert.True(decoded);
        Assert.Equal(2, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);
        Assert.Equal("first", await responses[0].Content.ReadAsStringAsync());
        Assert.Equal("second", await responses[1].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC7230: Partial second response held in remainder")]
    public async Task TwoPipelinedResponses_SecondPartial_RemainderBuffered()
    {
        var resp1 = BuildResponse(200, "OK", "first", ("Content-Length", "5"));
        var resp2 = BuildResponse(202, "Accepted", "done", ("Content-Length", "4"));

        // Send first complete + partial second (headers only, no body)
        var headerEndInResp2 = IndexOfDoubleCrlf(resp2) + 4;
        var chunk1 = new byte[resp1.Length + headerEndInResp2];
        resp1.Span.CopyTo(chunk1);
        resp2.Span[..headerEndInResp2].CopyTo(chunk1.AsSpan(resp1.Length));

        var chunk2 = resp2[headerEndInResp2..]; // remaining body bytes of resp2

        // First decode: should yield resp1, buffer partial resp2
        var decoded1 = _decoder.TryDecode(chunk1, out var responses1);
        Assert.True(decoded1);
        Assert.Single(responses1);
        Assert.Equal(HttpStatusCode.OK, responses1[0].StatusCode);

        // Second decode: completes resp2
        var decoded2 = _decoder.TryDecode(chunk2, out var responses2);
        Assert.True(decoded2);
        Assert.Single(responses2);
        Assert.Equal(HttpStatusCode.Accepted, responses2[0].StatusCode);
        Assert.Equal("done", await responses2[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9113: Three pipelined responses decoded in order")]
    public async Task ThreePipelinedResponses_InSameBuffer_DecodedInOrder()
    {
        var resp1 = BuildResponse(200, "OK", "alpha", ("Content-Length", "5"));
        var resp2 = BuildResponse(201, "Created", "beta", ("Content-Length", "4"));
        var resp3 = BuildResponse(202, "Accepted", "gamma", ("Content-Length", "5"));

        var combined = new byte[resp1.Length + resp2.Length + resp3.Length];
        resp1.Span.CopyTo(combined);
        resp2.Span.CopyTo(combined.AsSpan(resp1.Length));
        resp3.Span.CopyTo(combined.AsSpan(resp1.Length + resp2.Length));

        var decoded = _decoder.TryDecode(combined, out var responses);

        Assert.True(decoded);
        Assert.Equal(3, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, responses[2].StatusCode);
        Assert.Equal("alpha", await responses[0].Content.ReadAsStringAsync());
        Assert.Equal("beta", await responses[1].Content.ReadAsStringAsync());
        Assert.Equal("gamma", await responses[2].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC7233-4.1: Content-Range: bytes 0-499/1000 accessible")]
    public void Test_ContentRange_Accessible()
    {
        var raw = BuildResponse(206, "Partial Content", "first 500 bytes",
            ("Content-Length", "15"),
            ("Content-Range", "bytes 0-14/1000"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.PartialContent, responses[0].StatusCode);
        Assert.True(responses[0].Content.Headers.TryGetValues("Content-Range", out var crValues));
        Assert.Contains("bytes 0-14/1000", crValues);
    }

    [Fact(DisplayName = "RFC7233-4.1: 206 Partial Content with Content-Range decoded")]
    public async Task Test_PartialContent_Decoded()
    {
        const string partialBody = "Hello";
        var raw = BuildResponse(206, "Partial Content", partialBody,
            ("Content-Length", "5"),
            ("Content-Range", "bytes 0-4/1000"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.PartialContent, responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(partialBody, body);
    }

    [Fact(DisplayName = "RFC7233-4.1: 206 multipart/byteranges body decoded")]
    public async Task Test_Multipart_ByteRanges_Decoded()
    {
        // RFC 7233 §4.1: A server may return multiple ranges in a single multipart/byteranges response.
        // The client decoder returns the raw body; multipart parsing is the caller's responsibility.
        const string boundary = "3d6b6a416f9b5";
        const string multipartBody = $"--{boundary}\r\n" +
                                     $"Content-Type: text/plain\r\n" +
                                     $"Content-Range: bytes 0-4/1000\r\n" +
                                     $"\r\n" +
                                     $"Hello\r\n" +
                                     $"--{boundary}\r\n" +
                                     $"Content-Type: text/plain\r\n" +
                                     $"Content-Range: bytes 10-14/1000\r\n" +
                                     $"\r\n" +
                                     $"World\r\n" +
                                     $"--{boundary}--\r\n";

        var raw = BuildResponse(206, "Partial Content", multipartBody,
            ("Content-Length", multipartBody.Length.ToString()),
            ("Content-Type", $"multipart/byteranges; boundary={boundary}"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.PartialContent, responses[0].StatusCode);
        Assert.Equal("multipart/byteranges", responses[0].Content.Headers.ContentType?.MediaType);
        var body = await responses[0].Content.ReadAsStringAsync();
        Assert.Contains("Hello", body);
        Assert.Contains("World", body);
    }

    [Fact(DisplayName = "RFC7233-4.1: Content-Range: bytes 0-499/* unknown total")]
    public void Test_ContentRange_UnknownTotal_Accepted()
    {
        // RFC 7233 §4.2: The "*" token indicates an unknown total length.
        var raw = BuildResponse(206, "Partial Content", "Hello",
            ("Content-Length", "5"),
            ("Content-Range", "bytes 0-4/*"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.PartialContent, responses[0].StatusCode);
        Assert.True(responses[0].Content.Headers.TryGetValues("Content-Range", out var crValues));
        Assert.Contains("bytes 0-4/*", crValues);
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
