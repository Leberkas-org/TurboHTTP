using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http11RoundTripTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

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

    private static ReadOnlyMemory<byte> BuildChunkedResponse(int status, string reason,
        string[] chunks, (string Name, string Value)[]? trailers = null)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {reason}\r\n");
        sb.Append("Transfer-Encoding: chunked\r\n");
        sb.Append("\r\n");
        foreach (var chunk in chunks)
        {
            var chunkLen = Encoding.ASCII.GetByteCount(chunk);
            sb.Append($"{chunkLen:x}\r\n{chunk}\r\n");
        }

        sb.Append("0\r\n");
        if (trailers != null)
        {
            foreach (var (name, value) in trailers)
            {
                sb.Append($"{name}: {value}\r\n");
            }
        }

        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    // ── RT-11-001 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-001: HTTP/1.1 GET → 200 OK round-trip")]
    public async Task Should_Return200_When_GetRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.StartsWith("GET /api HTTP/1.1\r\n", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "hello", ("Content-Length", "5"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("hello", await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-002 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-002: HTTP/1.1 POST JSON → 201 Created round-trip")]
    public void Should_Return201Created_When_PostJsonRoundTrip()
    {
        const string json = "{\"name\":\"Alice\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/users")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("POST /users HTTP/1.1", encoded);
        Assert.Contains("Content-Type: application/json", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(201, "Created", "",
            ("Content-Length", "0"), ("Location", "/users/42"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.Created, responses[0].StatusCode);
        Assert.True(responses[0].Headers.TryGetValues("Location", out var loc));
        Assert.Equal("/users/42", loc.Single());
    }

    // ── RT-11-003 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-003: HTTP/1.1 PUT → 204 No Content round-trip")]
    public void Should_Return204NoContent_When_PutRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource/1")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("PUT /resource/1 HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
    }

    // ── RT-11-004 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-004: HTTP/1.1 DELETE → 200 OK round-trip")]
    public void Should_Return200_When_DeleteRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource/5");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("DELETE /resource/5 HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    // ── RT-11-005 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-005: HTTP/1.1 PATCH → 200 OK round-trip")]
    public async Task Should_Return200_When_PatchRoundTrip()
    {
        const string patch = "{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"Bob\"}";
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), "http://example.com/item/3")
        {
            Content = new StringContent(patch, Encoding.UTF8, "application/json-patch+json")
        };
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("PATCH /item/3 HTTP/1.1", encoded);

        const string responseBody = "{\"id\":3}";
        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", responseBody,
            ("Content-Length", responseBody.Length.ToString()),
            ("Content-Type", "application/json"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(responseBody, await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-006 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-006: HTTP/1.1 HEAD → Content-Length but no body")]
    public void Should_ReturnContentLengthHeader_When_HeadRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.StartsWith("HEAD /resource HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        // HEAD response: Content-Length=0; no body bytes follow
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Content-Type", "application/octet-stream"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    // ── RT-11-007 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-007: HTTP/1.1 OPTIONS → 200 with Allow header")]
    public void Should_ReturnAllowHeader_When_OptionsRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "http://example.com/resource");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("OPTIONS /resource HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Allow", "GET, POST, PUT, DELETE, OPTIONS"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.True(responses[0].Content.Headers.TryGetValues("Allow", out var allowVals));
        Assert.Contains("GET", string.Join(",", allowVals));
    }

    // ── RT-11-008 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-008: HTTP/1.1 GET → 200 chunked response round-trip")]
    public async Task Should_AssembleChunkedBody_When_ChunkedRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/stream");
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", ["Hello, ", "World!"]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("Hello, World!", await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-009 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-009: HTTP/1.1 GET → response with 5 chunks round-trip")]
    public async Task Should_ConcatenateChunks_When_FiveChunksRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/multi");
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", ["one", "two", "three", "four", "five"]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("onetwothreefourfive", await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-010 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-010: HTTP/1.1 chunked response with trailer round-trip")]
    public async Task Should_AccessTrailer_When_ChunkedWithTrailerRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/trailer");
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK",
            ["chunk1", "chunk2"],
            [("X-Checksum", "abc123")]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("chunk1chunk2", await responses[0].Content.ReadAsStringAsync());
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Checksum", out var trailerVals));
        Assert.Equal("abc123", trailerVals.Single());
    }

    // ── RT-11-011 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-011: HTTP/1.1 GET → 301 with Location round-trip")]
    public void Should_Return301WithLocation_When_GetRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old-path");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("GET /old-path HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(301, "Moved Permanently", "",
            ("Content-Length", "0"),
            ("Location", "http://example.com/new-path"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.MovedPermanently, responses[0].StatusCode);
        Assert.True(responses[0].Headers.TryGetValues("Location", out var loc));
        Assert.Contains("new-path", loc.Single());
    }

    // ── RT-11-012 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-012: HTTP/1.1 POST binary → 200 binary response round-trip")]
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

    // ── RT-11-013 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-013: HTTP/1.1 GET → 404 Not Found round-trip")]
    public async Task Should_Return404_When_ResourceMissingRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("GET /missing HTTP/1.1", encoded);

        const string body = "Not Found";
        var decoder = new Http11Decoder();
        var raw = BuildResponse(404, "Not Found", body, ("Content-Length", body.Length.ToString()));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NotFound, responses[0].StatusCode);
        Assert.Equal("Not Found", await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-014 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-014: HTTP/1.1 GET → 500 Internal Server Error round-trip")]
    public void Should_Return500_When_ServerErrorRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/error");
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(500, "Internal Server Error", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.InternalServerError, responses[0].StatusCode);
    }

    // ── RT-11-015 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-015: Two pipelined requests and responses round-trip")]
    public async Task Should_DecodeBothResponses_When_TwoPipelinedRequestsRoundTrip()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var (_, w1) = EncodeRequest(req1);
        var (_, w2) = EncodeRequest(req2);
        Assert.True(w1 > 0);
        Assert.True(w2 > 0);

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

    // ── RT-11-016 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-016: 100 Continue before 200 OK round-trip")]
    public async Task Should_SkipContinue_And_Return200_When_100ContinueRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new StringContent("Hello", Encoding.ASCII, "text/plain")
        };
        request.Headers.ExpectContinue = true;
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var continue100 = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
        var ok200 = BuildResponse(200, "OK", "done", ("Content-Length", "4"));
        var combined = new byte[continue100.Length + ok200.Length];
        continue100.CopyTo(combined, 0);
        ok200.Span.CopyTo(combined.AsSpan(continue100.Length));

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        // 100 Continue is skipped; only the 200 OK is returned
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("done", await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-017 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-017: HTTP/1.1 1 MB body round-trip")]
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

    // ── RT-11-018 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-018: HTTP/1.1 binary body with null bytes round-trip")]
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

    // ── RT-11-019 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-019: Two responses on keep-alive connection round-trip")]
    public async Task Should_DecodeSecondResponse_When_KeepAliveRoundTrip()
    {
        var decoder = new Http11Decoder();

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/first");
        var (_, w1) = EncodeRequest(req1);
        Assert.True(w1 > 0);

        var raw1 = BuildResponse(200, "OK", "first",
            ("Content-Length", "5"), ("Connection", "keep-alive"));
        decoder.TryDecode(raw1, out var responses1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/second");
        var (_, w2) = EncodeRequest(req2);
        Assert.True(w2 > 0);

        var raw2 = BuildResponse(200, "OK", "second",
            ("Content-Length", "6"), ("Connection", "keep-alive"));
        decoder.TryDecode(raw2, out var responses2);

        Assert.Single(responses1);
        Assert.Equal("first", await responses1[0].Content.ReadAsStringAsync());
        Assert.Single(responses2);
        Assert.Equal("second", await responses2[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-020 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-020: Content-Type: application/json; charset=utf-8 preserved")]
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

    // ── Helpers (extended) ─────────────────────────────────────────────────────

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

    // ── Content-Length Scenarios ───────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-021: Content-Length 0 — empty body decoded")]
    public async Task Should_ReturnEmptyBody_When_ContentLengthZeroRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RT-11-022: Content-Length matches UTF-8 byte count exactly")]
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

    [Fact(DisplayName = "RT-11-023: 64KB body round-trip with Content-Length")]
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

    [Fact(DisplayName = "RT-11-024: Three pipelined Content-Length responses decoded in order")]
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

    [Fact(DisplayName = "RT-11-025: Content-Length 1 — single byte body decoded")]
    public async Task Should_DecodeOneByte_When_ContentLengthOneRoundTrip()
    {
        var body = new byte[] { 0x42 };
        var decoder = new Http11Decoder();
        var raw = BuildBinaryResponse(200, "OK", body, ("Content-Length", "1"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(body, await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RT-11-026: Reset decoder — second Content-Length response decoded after reset")]
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

    // ── Chunked Transfer Encoding ───────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-027: Single 1-byte chunk decoded correctly")]
    public async Task Should_DecodeOneByte_When_SingleByteChunkRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", ["A"]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("A", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RT-11-028: Uppercase hex chunk size decoded correctly")]
    public async Task Should_DecodeBody_When_UppercaseHexChunkSizeRoundTrip()
    {
        // "A" = 10 in uppercase hex
        const string rawResponse =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "A\r\n" +
            "0123456789\r\n" +
            "0\r\n" +
            "\r\n";
        var mem = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(rawResponse);

        var decoder = new Http11Decoder();
        decoder.TryDecode(mem, out var responses);

        Assert.Single(responses);
        Assert.Equal("0123456789", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RT-11-029: 20 single-character chunks concatenated correctly")]
    public async Task Should_ConcatenateAllChunks_When_TwentyTinyChunksRoundTrip()
    {
        var chars = Enumerable.Range(0, 20).Select(i => ((char)('a' + i)).ToString()).ToArray();
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", chars);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        var expected = string.Concat(chars);
        Assert.Equal(expected, await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RT-11-030: 32KB single chunk decoded correctly")]
    public async Task Should_Preserve32KbChunk_When_LargeChunkRoundTrip()
    {
        var body = new string('X', 32768);
        var decoder = new Http11Decoder(maxBodySize: 32768 + 1024);
        var raw = BuildChunkedResponse(200, "OK", [body]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        var decoded = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(32768, decoded.Length);
        Assert.All(decoded, c => Assert.Equal('X', c));
    }

    [Fact(DisplayName = "RT-11-031: Chunk with extension token — body decoded correctly (RFC 9112 §7.1.1)")]
    public async Task Should_DecodeBody_When_ChunkHasExtensionRoundTrip()
    {
        const string rawResponse =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "5;ext=value\r\n" +
            "Hello\r\n" +
            "6;checksum=abc\r\n" +
            " World\r\n" +
            "0\r\n" +
            "\r\n";
        var mem = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(rawResponse);

        var decoder = new Http11Decoder();
        decoder.TryDecode(mem, out var responses);

        Assert.Single(responses);
        Assert.Equal("Hello World", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RT-11-032: Pipelined chunked then Content-Length response decoded")]
    public async Task Should_DecodeBoth_When_ChunkedThenContentLengthPipelined()
    {
        var chunked = BuildChunkedResponse(200, "OK", ["chunk-data"]);
        var fixedLen = BuildResponse(201, "Created", "fixed", ("Content-Length", "5"));
        var combined = Combine(chunked, fixedLen);

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(2, responses.Count);
        Assert.Equal("chunk-data", await responses[0].Content.ReadAsStringAsync());
        Assert.Equal("fixed", await responses[1].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RT-11-033: Chunked body with two trailer headers round-trip")]
    public async Task Should_AccessBothTrailers_When_TwoTrailerHeadersRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK",
            ["part1", "part2"],
            [("X-Digest", "sha256:abc"), ("X-Request-Id", "req-999")]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("part1part2", await responses[0].Content.ReadAsStringAsync());
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Digest", out var digest));
        Assert.Equal("sha256:abc", digest.Single());
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Request-Id", out var reqId));
        Assert.Equal("req-999", reqId.Single());
    }

    // ── Pipelining ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-034: Three pipelined responses decoded in order")]
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

    [Fact(DisplayName = "RT-11-035: Five pipelined responses all decoded correctly")]
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

    [Fact(DisplayName = "RT-11-036: Pipelined 200 → 404 → 200 — status codes preserved")]
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

    [Fact(DisplayName = "RT-11-037: Pipelined 204 → 200 → 204 — no-body responses handled")]
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

    [Fact(DisplayName = "RT-11-038: Pipelined chunked → Content-Length → 204 all decoded")]
    public async Task Should_DecodeAll_When_MixedEncodingsPipelined()
    {
        var r1 = BuildChunkedResponse(200, "OK", ["chunked"]);
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

    // ── HEAD Requests ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-039: TryDecodeHead — Content-Length present but body not consumed")]
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

    [Fact(DisplayName = "RT-11-040: TryDecodeHead 404 — empty body returned")]
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

    [Fact(DisplayName = "RT-11-041: Two pipelined HEAD responses via TryDecodeHead")]
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

    [Fact(DisplayName = "RT-11-042: HEAD 200 then GET 200 on same decoder instance")]
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

    // ── No-body Responses ──────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-043: 304 Not Modified with ETag — no body, ETag header preserved")]
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

    [Fact(DisplayName = "RT-11-044: 204 No Content after DELETE — empty body")]
    public async Task Should_Return204EmptyBody_When_DeleteReturnsNoContent()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource/99");
        var (buffer, written) = EncodeRequest(request);
        Assert.Contains("DELETE", Encoding.ASCII.GetString(buffer, 0, written));

        var decoder = new Http11Decoder();
        var raw = BuildResponse(204, "No Content", "");
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "RT-11-045: Pipelined 304 → 200 — body only in 200 decoded")]
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

    [Fact(DisplayName = "RT-11-046: 102 Processing skipped — only 200 OK returned")]
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

    [Fact(DisplayName = "RT-11-047: 204 with Content-Type header — empty body returned")]
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

    // ── Keep-alive vs. Close ───────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-048: Connection: close header preserved in decoded response")]
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

    [Fact(DisplayName = "RT-11-049: Three sequential keep-alive responses decoded correctly")]
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

    [Fact(DisplayName = "RT-11-050: Reset() clears state — fresh response decoded after reset")]
    public async Task Should_DecodeCorrectly_When_DecoderResetBetweenConnections()
    {
        using var decoder = new Http11Decoder();

        var r1 = BuildResponse(200, "OK", "before", ("Content-Length", "6"));
        decoder.TryDecode(r1, out _);
        decoder.Reset();

        var r2 = BuildResponse(200, "OK", "after", ("Content-Length", "5"));
        var decoded = decoder.TryDecode(r2, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal("after", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RT-11-051: Keep-alive — varying body sizes all decoded correctly")]
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

    // ── TCP Fragmentation ──────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-052: TCP fragment split after status line CRLF — response assembled")]
    public async Task Should_AssembleResponse_When_SplitAfterStatusLine()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(full);

        // "HTTP/1.1 200 OK\r\n" = 17 bytes
        const int splitAt = 17;
        var part1 = new ReadOnlyMemory<byte>(bytes, 0, splitAt);
        var part2 = new ReadOnlyMemory<byte>(bytes, splitAt, bytes.Length - splitAt);

        var decoder = new Http11Decoder();
        var decoded1 = decoder.TryDecode(part1, out _);
        var decoded2 = decoder.TryDecode(part2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        Assert.Single(responses);
        Assert.Equal("hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RT-11-053: TCP fragment split at header-body boundary — response assembled")]
    public async Task Should_AssembleResponse_When_SplitAtHeaderBodyBoundary()
    {
        var headerBytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\n");
        var bodyBytes = Encoding.ASCII.GetBytes("hello");

        var decoder = new Http11Decoder();
        decoder.TryDecode(headerBytes, out _);
        decoder.TryDecode(bodyBytes, out var responses);

        Assert.Single(responses);
        Assert.Equal("hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RT-11-054: TCP fragment split mid-body — body assembled correctly")]
    public async Task Should_AssembleBody_When_SplitMidBody()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\n0123456789";
        var bytes = Encoding.ASCII.GetBytes(full);
        var headerLen = full.IndexOf("\r\n\r\n") + 4;

        // Split 5 bytes into the body
        var splitAt = headerLen + 5;
        var part1 = new ReadOnlyMemory<byte>(bytes, 0, splitAt);
        var part2 = new ReadOnlyMemory<byte>(bytes, splitAt, bytes.Length - splitAt);

        var decoder = new Http11Decoder();
        decoder.TryDecode(part1, out _);
        decoder.TryDecode(part2, out var responses);

        Assert.Single(responses);
        Assert.Equal("0123456789", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RT-11-055: Single-byte TCP delivery assembles complete response")]
    public async Task Should_AssembleResponse_When_SingleByteTcpDelivery()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\nabc";
        var bytes = Encoding.ASCII.GetBytes(full);

        var decoder = new Http11Decoder();
        HttpResponseMessage? finalResponse = null;

        for (var i = 0; i < bytes.Length; i++)
        {
            var chunk = new ReadOnlyMemory<byte>(bytes, i, 1);
            if (decoder.TryDecode(chunk, out var r) && r.Count > 0)
            {
                finalResponse = r[0];
            }
        }

        Assert.NotNull(finalResponse);
        Assert.Equal("abc", await finalResponse!.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RT-11-056: TCP fragment split between two chunks — body assembled correctly")]
    public async Task Should_AssembleChunkedBody_When_SplitBetweenChunks()
    {
        var part1 = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nfoo\r\n");
        var part2 = (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(
            "3\r\nbar\r\n0\r\n\r\n");

        var decoder = new Http11Decoder();
        decoder.TryDecode(part1, out _);
        decoder.TryDecode(part2, out var responses);

        Assert.Single(responses);
        Assert.Equal("foobar", await responses[0].Content.ReadAsStringAsync());
    }

    // ── Miscellaneous / Edge Cases ─────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-057: 503 Service Unavailable with Retry-After header preserved")]
    public void Should_Return503WithRetryAfter_When_ServiceUnavailableRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/busy");
        var (buffer, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(503, "Service Unavailable", "",
            ("Content-Length", "0"),
            ("Retry-After", "120"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, responses[0].StatusCode);
        Assert.True(responses[0].Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.Equal("120", retryAfter.Single());
    }

    [Fact(DisplayName = "RT-11-058: Response with 10 custom headers — all preserved")]
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

    [Fact(DisplayName = "RT-11-059: UTF-8 body preserved byte-for-byte round-trip")]
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

    [Fact(DisplayName = "RT-11-060: Request URL with query string — path and query preserved")]
    public void Should_EncodeQueryString_When_RequestHasQueryStringRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "http://example.com/search?q=hello+world&page=1");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);

        Assert.Contains("GET /search?q=hello+world&page=1 HTTP/1.1", encoded);
    }

    [Fact(DisplayName = "RT-11-061: ETag with quotes and Cache-Control preserved exactly")]
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
}
