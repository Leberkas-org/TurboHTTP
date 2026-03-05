using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11RoundTripNoBodyTests
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

    [Fact(DisplayName = "RFC7230-3.3: 304 Not Modified with ETag — no body, ETag header preserved")]
    public void Should_Return304NoBody_When_NotModifiedWithETagRoundTrip()
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

    [Fact(DisplayName = "RFC7230-3.3: 204 No Content after DELETE — empty body")]
    public async Task Should_Return204EmptyBody_When_DeleteReturnsNoContent()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(204, "No Content", "");
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: Pipelined 304 → 200 — body only in 200 decoded")]
    public async Task Should_DecodeBodyOf200_When_304PrecededIt()
    {
        var r304 = BuildResponse(304, "Not Modified", "");
        var r200 = BuildResponse(200, "OK", "fresh", ("Content-Length", "5"));
        var combined = Combine(r304, r200);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(2, responses.Count);
        Assert.Equal(HttpStatusCode.NotModified, responses[0].StatusCode);
        Assert.Equal("fresh", await responses[1].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: 204 with Content-Type header — empty body returned")]
    public async Task Should_ReturnEmptyBody_When_204HasContentTypeHeader()
    {
        var raw = BuildResponse(204, "No Content", "",
            ("Content-Type", "application/json"));

        var decoder = new Http11Decoder();
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: Pipelined 204 → 200 → 204 — no-body responses handled")]
    public async Task Should_DecodeAll_When_PipelineContainsNoBodyResponses()
    {
        var r1 = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        var r2 = BuildResponse(200, "OK", "data", ("Content-Length", "4"));
        var r3 = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        var combined = Combine(r1, r2, r3);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(3, responses.Count);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Equal("data", await responses[1].Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, responses[2].StatusCode);
    }

    [Fact(DisplayName = "RFC9110-8.3.4: TryDecodeHead — Content-Length present but body not consumed")]
    public async Task Should_ReturnEmptyBody_When_HeadResponseHasContentLength()
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
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC9110-8.3.4: TryDecodeHead 404 — empty body returned")]
    public async Task Should_Return404EmptyBody_When_HeadResponseIs404()
    {
        const string rawResponse = "HTTP/1.1 404 Not Found\r\nContent-Length: 50\r\n\r\n";
        var mem = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(rawResponse);

        var decoder = new Http11Decoder();
        decoder.TryDecodeHead(mem, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NotFound, responses[0].StatusCode);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC9110-8.3.4: Two pipelined HEAD responses via TryDecodeHead")]
    public async Task Should_DecodeBothHeads_When_TwoHeadResponsesPipelined()
    {
        const string rawResponse =
            "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\n" +
            "HTTP/1.1 200 OK\r\nContent-Length: 200\r\n\r\n";
        var mem = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(rawResponse);

        var decoder = new Http11Decoder();
        decoder.TryDecodeHead(mem, out var responses);

        Assert.Equal(2, responses.Count);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync());
        Assert.Empty(await responses[1].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC9110-8.3.4: HEAD 200 then GET 200 on same decoder instance")]
    public async Task Should_DecodeGetAfterHead_When_SameDecoderUsedForBoth()
    {
        var decoder = new Http11Decoder();

        const string headRaw = "HTTP/1.1 200 OK\r\nContent-Length: 42\r\n\r\n";
        decoder.TryDecodeHead((ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(headRaw), out var headResp);
        Assert.Single(headResp);
        Assert.Empty(await headResp[0].Content.ReadAsByteArrayAsync());

        var getRaw = BuildResponse(200, "OK", "actual body", ("Content-Length", "11"));
        decoder.TryDecode(getRaw, out var getResp);
        Assert.Single(getResp);
        Assert.Equal("actual body", await getResp[0].Content.ReadAsStringAsync());
    }
}
