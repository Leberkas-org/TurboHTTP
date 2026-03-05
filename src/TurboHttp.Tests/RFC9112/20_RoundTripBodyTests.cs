using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11RoundTripBodyTests
{
    private static (byte[] Buffer, int Written) EncodeRequest(HttpRequestMessage request)
    {
        var buffer = new byte[65536];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);
        return (buffer, written);
    }

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

    private static ReadOnlyMemory<byte> BuildBinaryResponse(int status, string reason, byte[] body,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
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

    [Fact(DisplayName = "RFC7230-3.3: HTTP/1.1 POST binary → 200 binary response round-trip")]
    public async Task Should_PreserveBinaryBody_When_PostBinaryRoundTrip()
    {
        var binary = new byte[256];
        for (var i = 0; i < 256; i++) { binary[i] = (byte)i; }

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload")
        {
            Content = new ByteArrayContent(binary)
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var (buffer, written) = EncodeRequest(request);
        Assert.Contains("POST", Encoding.ASCII.GetString(buffer, 0, 20));

        var decoder = new Http11Decoder();
        var raw = BuildBinaryResponse(200, "OK", binary, ("Content-Length", "256"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(binary, await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: HTTP/1.1 1 MB body round-trip")]
    public async Task Should_Preserve1MbBody_When_LargeBodyRoundTrip()
    {
        const int oneMb = 1024 * 1024;
        var body = new byte[oneMb];
        for (var i = 0; i < oneMb; i++) { body[i] = (byte)(i % 256); }

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload")
        {
            Content = new ByteArrayContent(body)
        };
        var encBuf = new byte[oneMb + 4096];
        var span = encBuf.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);
        Assert.True(written > oneMb);

        var decoder = new Http11Decoder(maxBodySize: oneMb + 1024);
        var raw = BuildBinaryResponse(200, "OK", body, ("Content-Length", oneMb.ToString()));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(body, await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: HTTP/1.1 binary body with null bytes round-trip")]
    public async Task Should_PreserveNullBytes_When_BinaryBodyRoundTrip()
    {
        var body = new byte[] { 0x00, 0x01, 0x00, 0xFF, 0x00, 0x7F, 0x00 };
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/binary")
        {
            Content = new ByteArrayContent(body)
        };
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var decoder = new Http11Decoder();
        var raw = BuildBinaryResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(body, await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: Content-Length 0 — empty body decoded")]
    public async Task Should_ReturnEmptyBody_When_ContentLengthZeroRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: Content-Length matches UTF-8 byte count exactly")]
    public async Task Should_DecodeUtf8Body_When_ContentLengthMatchesBytes()
    {
        const string text = "日本語テスト";
        var bodyBytes = Encoding.UTF8.GetBytes(text);
        var decoder = new Http11Decoder();
        var raw = BuildBinaryResponse(200, "OK", bodyBytes,
            ("Content-Length", bodyBytes.Length.ToString()),
            ("Content-Type", "text/plain; charset=utf-8"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(bodyBytes, await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: 64KB body round-trip with Content-Length")]
    public async Task Should_Preserve64KbBody_When_ContentLengthRoundTrip()
    {
        var body = new byte[65536];
        for (var i = 0; i < body.Length; i++) { body[i] = (byte)(i & 0xFF); }

        var decoder = new Http11Decoder(maxBodySize: 65536 + 1024);
        var raw = BuildBinaryResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(body, await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: Three pipelined Content-Length responses decoded in order")]
    public async Task Should_DecodeAll_When_ThreePipelinedContentLengthRoundTrip()
    {
        var r1 = BuildResponse(200, "OK", "one", ("Content-Length", "3"));
        var r2 = BuildResponse(202, "Accepted", "two", ("Content-Length", "3"));
        var r3 = BuildResponse(200, "OK", "three", ("Content-Length", "5"));
        var combined = Combine(r1, r2, r3);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(3, responses.Count);
        Assert.Equal("one", await responses[0].Content.ReadAsStringAsync());
        Assert.Equal("two", await responses[1].Content.ReadAsStringAsync());
        Assert.Equal("three", await responses[2].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: Content-Length 1 — single byte body decoded")]
    public async Task Should_DecodeOneByte_When_ContentLengthOneRoundTrip()
    {
        var body = new byte[] { 0x42 };
        var decoder = new Http11Decoder();
        var raw = BuildBinaryResponse(200, "OK", body, ("Content-Length", "1"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(body, await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: Reset decoder — second Content-Length response decoded after reset")]
    public async Task Should_DecodeAfterReset_When_ContentLengthRoundTrip()
    {
        var decoder = new Http11Decoder();
        var r1 = BuildResponse(200, "OK", "first", ("Content-Length", "5"));
        decoder.TryDecode(r1, out _);
        decoder.Reset();

        var r2 = BuildResponse(200, "OK", "second", ("Content-Length", "6"));
        var decoded = decoder.TryDecode(r2, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal("second", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC7230-3.3: Keep-alive — varying body sizes all decoded correctly")]
    public async Task Should_DecodeAllSizes_When_KeepAliveVaryingBodySizes()
    {
        var decoder = new Http11Decoder();
        var sizes = new[] { 1, 10, 100, 1000 };

        foreach (var size in sizes)
        {
            var body = new string('A', size);
            var raw = BuildResponse(200, "OK", body, ("Content-Length", size.ToString()));
            decoder.TryDecode(raw, out var responses);

            Assert.Single(responses);
            Assert.Equal(size, (await responses[0].Content.ReadAsStringAsync()).Length);
        }
    }

    [Fact(DisplayName = "RFC7230-3.3: Content-Type: application/json; charset=utf-8 preserved")]
    public void Should_PreserveContentType_When_JsonCharsetRoundTrip()
    {
        const string json = "{\"key\":\"value\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Content-Type: application/json", encoded);

        var byteCount = Encoding.UTF8.GetByteCount(json);
        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", json,
            ("Content-Length", byteCount.ToString()),
            ("Content-Type", "application/json; charset=utf-8"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("application/json", responses[0].Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", responses[0].Content.Headers.ContentType!.CharSet);
    }

    [Fact(DisplayName = "RFC7230-3.3: UTF-8 body preserved byte-for-byte round-trip")]
    public async Task Should_PreserveUtf8Bytes_When_Utf8BodyRoundTrip()
    {
        const string text = "Hello, 世界! Привет мир!";
        var bodyBytes = Encoding.UTF8.GetBytes(text);

        var decoder = new Http11Decoder();
        var raw = BuildBinaryResponse(200, "OK", bodyBytes,
            ("Content-Length", bodyBytes.Length.ToString()),
            ("Content-Type", "text/plain; charset=utf-8"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        var decoded = Encoding.UTF8.GetString(await responses[0].Content.ReadAsByteArrayAsync());
        Assert.Equal(text, decoded);
    }

    [Fact(DisplayName = "RFC7230-3.3: ETag with quotes and Cache-Control preserved exactly")]
    public void Should_PreserveETagAndCacheControl_When_ETagResponseRoundTrip()
    {
        var raw = BuildResponse(200, "OK", "data",
            ("Content-Length", "4"),
            ("ETag", "\"v1.0-abc123\""),
            ("Cache-Control", "max-age=3600"));

        var decoder = new Http11Decoder();
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.True(responses[0].Headers.TryGetValues("ETag", out var etag));
        Assert.Equal("\"v1.0-abc123\"", etag.Single());
        Assert.True(responses[0].Headers.TryGetValues("Cache-Control", out var cc));
        Assert.Equal("max-age=3600", cc.Single());
    }

    [Fact(DisplayName = "RFC7230-3.3: Response with 10 custom headers — all preserved")]
    public void Should_PreserveAllHeaders_When_ResponseHasTenCustomHeaders()
    {
        var headers = new (string Name, string Value)[11];
        for (var i = 1; i <= 10; i++)
        {
            headers[i - 1] = ($"X-Custom-{i}", $"value-{i}");
        }

        headers[10] = ("Content-Length", "0");

        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "", headers);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        for (var i = 1; i <= 10; i++)
        {
            Assert.True(responses[0].Headers.TryGetValues($"X-Custom-{i}", out var vals));
            Assert.Equal($"value-{i}", vals.Single());
        }
    }
}
