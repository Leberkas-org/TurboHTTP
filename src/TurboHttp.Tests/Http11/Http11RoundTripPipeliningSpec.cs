using System.Net;
using System.Text;
using TurboHttp.Protocol.Http11;

namespace TurboHttp.Tests.Http11;

/// <summary>
/// Tests HTTP/1.1 request pipelining round-trips per RFC 9112 §9.3.
/// Verifies that multiple consecutive requests and responses are correctly correlated.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="Http11Encoder"/> and <see cref="Http11Decoder"/>.
/// RFC 9112 §9.3: Pipelining — responses MUST be sent in the same order as requests.
/// </remarks>
public sealed class Http11RoundTripPipeliningSpec
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public async Task Http11RoundTrip_should_decode_both_responses_when_two_pipelined_requests_round_trip()
    {
        var resp1 = BuildResponse(200, "OK", "alpha", ("Content-Length", "5"));
        var resp2 = BuildResponse(200, "OK", "beta", ("Content-Length", "4"));
        var combined = new byte[resp1.Length + resp2.Length];
        resp1.Span.CopyTo(combined);
        resp2.Span.CopyTo(combined.AsSpan(resp1.Length));

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(2, responses.Count);
        Assert.Equal("alpha", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("beta", await responses[1].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public async Task Http11RoundTrip_should_decode_all_three_when_three_pipelined_responses_round_trip()
    {
        var r1 = BuildResponse(200, "OK", "alpha", ("Content-Length", "5"));
        var r2 = BuildResponse(200, "OK", "beta", ("Content-Length", "4"));
        var r3 = BuildResponse(200, "OK", "gamma", ("Content-Length", "5"));
        var combined = Combine(r1, r2, r3);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(3, responses.Count);
        Assert.Equal("alpha", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("beta", await responses[1].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("gamma", await responses[2].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public async Task Http11RoundTrip_should_decode_all_five_when_five_pipelined_responses_round_trip()
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
            Assert.Equal($"r{i + 1}", await decoded[i].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    [Trait("RFC", "RFC9112-9")]
    public void Http11RoundTrip_should_preserve_status_codes_when_mixed_status_pipelined()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public async Task Http11RoundTrip_should_skip_continue_and_return_200_when_100_continue_round_trip()
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
        Assert.Equal("done", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public async Task Http11RoundTrip_should_skip_102_when_followed_by_200_round_trip()
    {
        const string combined =
            "HTTP/1.1 102 Processing\r\n\r\n" +
            "HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\ndone";
        var mem = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(combined);

        var decoder = new Http11Decoder();
        decoder.TryDecode(mem, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("done", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public async Task Http11RoundTrip_should_decode_second_response_when_keep_alive_round_trip()
    {
        var decoder = new Http11Decoder();

        var raw1 = BuildResponse(200, "OK", "first",
            ("Content-Length", "5"), ("Connection", "keep-alive"));
        decoder.TryDecode(raw1, out var responses1);

        var raw2 = BuildResponse(200, "OK", "second",
            ("Content-Length", "6"), ("Connection", "keep-alive"));
        decoder.TryDecode(raw2, out var responses2);

        Assert.Single(responses1);
        Assert.Equal("first", await responses1[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Single(responses2);
        Assert.Equal("second", await responses2[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public async Task Http11RoundTrip_should_decode_all_three_when_sequential_keep_alive_round_trip()
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
            Assert.Equal(body, await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    [Trait("RFC", "RFC9112-9")]
    public void Http11RoundTrip_should_return_connection_close_when_response_has_connection_close_header()
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public async Task Http11RoundTrip_should_decode_all_when_mixed_encodings_pipelined()
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
        Assert.Equal("chunked", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("fixed", await responses[1].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NoContent, responses[2].StatusCode);
    }
}
