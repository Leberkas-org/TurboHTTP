using System.Net;
using System.Text;
using TurboHttp.Protocol.Http11;

namespace TurboHttp.Tests.Http11;

/// <summary>
/// Tests round-trip encoding and decoding of no-body responses per RFC 9112 §6.3.
/// Verifies that 1xx, 204, and 304 responses produce empty bodies end-to-end.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="Http11Encoder"/> and <see cref="Http11Decoder"/>.
/// RFC 9112 §6.3: Body not allowed for 1xx, 204 No Content, and 304 Not Modified responses.
/// </remarks>
public sealed class Http11RoundTripNoBodySpec
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

    [Fact]
    [Trait("RFC", "RFC9112-6")]
    public void Http11RoundTrip_should_return_304_no_body_when_not_modified_with_etag_round_trip()
    {
        var raw = BuildResponse(304, "Not Modified", "",
            ("ETag", "\"abc123\""),
            ("Last-Modified", "Wed, 01 Jan 2025 00:00:00 GMT"));

        var decoder = new Http11Decoder();
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NotModified, responses[0].StatusCode);
        Assert.True(responses[0].Headers.TryGetValues("ETag", out var etag));
        Assert.Equal("\"abc123\"", etag.Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTrip_should_return_204_empty_body_when_delete_returns_no_content()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(204, "No Content", "");
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTrip_should_decode_body_of_200_when_304_preceded_it()
    {
        var r304 = BuildResponse(304, "Not Modified", "");
        var r200 = BuildResponse(200, "OK", "fresh", ("Content-Length", "5"));
        var combined = Combine(r304, r200);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(2, responses.Count);
        Assert.Equal(HttpStatusCode.NotModified, responses[0].StatusCode);
        Assert.Equal("fresh", await responses[1].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTrip_should_return_empty_body_when_204_has_content_type_header()
    {
        var raw = BuildResponse(204, "No Content", "",
            ("Content-Type", "application/json"));

        var decoder = new Http11Decoder();
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTrip_should_decode_all_when_pipeline_contains_no_body_responses()
    {
        var r1 = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        var r2 = BuildResponse(200, "OK", "data", ("Content-Length", "4"));
        var r3 = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        var combined = Combine(r1, r2, r3);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(3, responses.Count);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Equal("data", await responses[1].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NoContent, responses[2].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTrip_should_return_empty_body_when_head_response_has_content_length()
    {
        const string rawResponse =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 100\r\n" +
            "Content-Type: application/json\r\n" +
            "\r\n";
        var mem = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(rawResponse);

        var decoder = new Http11Decoder();
        var decoded = decoder.TryDecodeHead(mem, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTrip_should_return_404_empty_body_when_head_response_is_404()
    {
        const string rawResponse = "HTTP/1.1 404 Not Found\r\nContent-Length: 50\r\n\r\n";
        var mem = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(rawResponse);

        var decoder = new Http11Decoder();
        decoder.TryDecodeHead(mem, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NotFound, responses[0].StatusCode);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTrip_should_decode_both_heads_when_two_head_responses_pipelined()
    {
        const string rawResponse =
            "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\n" +
            "HTTP/1.1 200 OK\r\nContent-Length: 200\r\n\r\n";
        var mem = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(rawResponse);

        var decoder = new Http11Decoder();
        decoder.TryDecodeHead(mem, out var responses);

        Assert.Equal(2, responses.Count);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await responses[1].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTrip_should_decode_get_after_head_when_same_decoder_used_for_both()
    {
        var decoder = new Http11Decoder();

        const string headRaw = "HTTP/1.1 200 OK\r\nContent-Length: 42\r\n\r\n";
        decoder.TryDecodeHead((ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(headRaw), out var headResp);
        Assert.Single(headResp);
        Assert.Empty(await headResp[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        var getRaw = BuildResponse(200, "OK", "actual body", ("Content-Length", "11"));
        decoder.TryDecode(getRaw, out var getResp);
        Assert.Single(getResp);
        Assert.Equal("actual body", await getResp[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
