using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11RoundTripPipeliningTests
{
    private static ReadOnlyMemory<byte> BuildResponse(int status, string reason, string body,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static ReadOnlyMemory<byte> Combine(params ReadOnlyMemory<byte>[] parts)
    {
        var totalLen = parts.Sum(p => p.Length);
        var result = new byte[totalLen];
        var offset = 0;
        foreach (var part in parts)
        {
            part.Span.CopyTo(result.AsSpan(offset));
            offset += part.Length;
        }

        return result;
    }

    [Fact(DisplayName = "RFC7230-5.1: Two pipelined requests and responses round-trip")]
    public async Task Should_DecodeBothResponses_When_TwoPipelinedRequestsRoundTrip()
    {
        var resp1 = BuildResponse(200, "OK", "alpha", ("Content-Length", "5"));
        var resp2 = BuildResponse(200, "OK", "beta", ("Content-Length", "4"));
        var combined = new byte[resp1.Length + resp2.Length];
        resp1.Span.CopyTo(combined);
        resp2.Span.CopyTo(combined.AsSpan(resp1.Length));

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(2, responses.Count);
        Assert.Equal("alpha", await responses[0].Content.ReadAsStringAsync());
        Assert.Equal("beta", await responses[1].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC7230-5.1: Three pipelined responses decoded in order")]
    public async Task Should_DecodeAllThree_When_ThreePipelinedResponsesRoundTrip()
    {
        var r1 = BuildResponse(200, "OK", "alpha", ("Content-Length", "5"));
        var r2 = BuildResponse(200, "OK", "beta", ("Content-Length", "4"));
        var r3 = BuildResponse(200, "OK", "gamma", ("Content-Length", "5"));
        var combined = Combine(r1, r2, r3);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(3, responses.Count);
        Assert.Equal("alpha", await responses[0].Content.ReadAsStringAsync());
        Assert.Equal("beta", await responses[1].Content.ReadAsStringAsync());
        Assert.Equal("gamma", await responses[2].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC7230-5.1: Five pipelined responses all decoded correctly")]
    public async Task Should_DecodeAllFive_When_FivePipelinedResponsesRoundTrip()
    {
        var parts = Enumerable.Range(1, 5)
            .Select(i => BuildResponse(200, "OK", $"r{i}", ("Content-Length", "2")))
            .ToArray();
        var combined = Combine(parts);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var decoded);

        Assert.Equal(5, decoded.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal($"r{i + 1}", await decoded[i].Content.ReadAsStringAsync());
        }
    }

    [Fact(DisplayName = "RFC7230-5.1: Pipelined 200 → 404 → 200 — status codes preserved")]
    public void Should_PreserveStatusCodes_When_MixedStatusPipelined()
    {
        var r1 = BuildResponse(200, "OK", "ok", ("Content-Length", "2"));
        var r2 = BuildResponse(404, "Not Found", "nf", ("Content-Length", "2"));
        var r3 = BuildResponse(200, "OK", "ok", ("Content-Length", "2"));
        var combined = Combine(r1, r2, r3);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(3, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, responses[1].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[2].StatusCode);
    }

    [Fact(DisplayName = "RFC7230-5.1: HTTP/1.1 1xx status skipped, final status returned")]
    public async Task Should_SkipContinue_And_Return200_When_100ContinueRoundTrip()
    {
        var continue100 = "HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray();
        var ok200Sb = new StringBuilder();
        ok200Sb.Append("HTTP/1.1 200 OK\r\n");
        ok200Sb.Append("Content-Length: 4\r\n");
        ok200Sb.Append("\r\n");
        ok200Sb.Append("done");
        var ok200 = (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(ok200Sb.ToString());
        var combined = new byte[continue100.Length + ok200.Length];
        continue100.CopyTo(combined, 0);
        ok200.Span.CopyTo(combined.AsSpan(continue100.Length));

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("done", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC7230-5.1: 102 Processing skipped — only 200 OK returned")]
    public async Task Should_Skip102_When_FollowedBy200RoundTrip()
    {
        const string combined =
            "HTTP/1.1 102 Processing\r\n\r\n" +
            "HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\ndone";
        var mem = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(combined);

        var decoder = new Http11Decoder();
        decoder.TryDecode(mem, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("done", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC7230-6.1: Two sequential keep-alive responses decoded correctly")]
    public async Task Should_DecodeSecondResponse_When_KeepAliveRoundTrip()
    {
        var decoder = new Http11Decoder();

        var raw1 = BuildResponse(200, "OK", "first",
            ("Content-Length", "5"), ("Connection", "keep-alive"));
        decoder.TryDecode(raw1, out var responses1);

        var raw2 = BuildResponse(200, "OK", "second",
            ("Content-Length", "6"), ("Connection", "keep-alive"));
        decoder.TryDecode(raw2, out var responses2);

        Assert.Single(responses1);
        Assert.Equal("first", await responses1[0].Content.ReadAsStringAsync());
        Assert.Single(responses2);
        Assert.Equal("second", await responses2[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC7230-6.1: Three sequential keep-alive responses decoded correctly")]
    public async Task Should_DecodeAllThree_When_SequentialKeepAliveRoundTrip()
    {
        var decoder = new Http11Decoder();

        for (var i = 1; i <= 3; i++)
        {
            var body = $"resp{i}";
            var raw = BuildResponse(200, "OK", body,
                ("Content-Length", body.Length.ToString()),
                ("Connection", "keep-alive"));
            decoder.TryDecode(raw, out var responses);

            Assert.Single(responses);
            Assert.Equal(body, await responses[0].Content.ReadAsStringAsync());
        }
    }

    [Fact(DisplayName = "RFC7230-6.1: Connection: close header preserved in decoded response")]
    public void Should_ReturnConnectionClose_When_ResponseHasConnectionCloseHeader()
    {
        var raw = BuildResponse(200, "OK", "data",
            ("Content-Length", "4"),
            ("Connection", "close"));

        var decoder = new Http11Decoder();
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.True(responses[0].Headers.TryGetValues("Connection", out var conn));
        Assert.Contains("close", conn.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "RFC7230-6.1: Pipelined chunked → Content-Length → 204 all decoded")]
    public async Task Should_DecodeAll_When_MixedEncodingsPipelined()
    {
        var sb1 = new StringBuilder();
        sb1.Append("HTTP/1.1 200 OK\r\n");
        sb1.Append("Transfer-Encoding: chunked\r\n");
        sb1.Append("\r\n");
        var chunkLen = Encoding.ASCII.GetByteCount("chunked");
        sb1.Append($"{chunkLen:x}\r\nchunked\r\n0\r\n\r\n");
        var r1 = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(sb1.ToString());

        var r2 = BuildResponse(200, "OK", "fixed", ("Content-Length", "5"));
        var r3 = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        var combined = Combine(r1, r2, r3);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(3, responses.Count);
        Assert.Equal("chunked", await responses[0].Content.ReadAsStringAsync());
        Assert.Equal("fixed", await responses[1].Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, responses[2].StatusCode);
    }
}
