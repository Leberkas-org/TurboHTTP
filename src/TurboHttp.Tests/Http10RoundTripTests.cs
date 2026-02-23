using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public class Http10RoundTripTests
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    private static ReadOnlyMemory<byte> BuildRawResponse(string statusLine, string headers, string body = "")
    {
        var raw = $"{statusLine}\r\n{headers}\r\n\r\n{body}";
        return Bytes(raw);
    }

    private static ReadOnlyMemory<byte> BuildRawResponse(string statusLine, string headers, byte[] body)
    {
        var headerPart = Encoding.ASCII.GetBytes($"{statusLine}\r\n{headers}\r\n\r\n");
        var result = new byte[headerPart.Length + body.Length];
        headerPart.CopyTo(result, 0);
        body.CopyTo(result, headerPart.Length);
        return result;
    }

    [Fact]
    public void Roundtrip_GetRequest_CorrectRequestLineInOutput()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/data");
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encodedRequest = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.StartsWith("GET /api/data HTTP/1.0\r\n", encodedRequest);
        Assert.Contains("\r\n\r\n", encodedRequest);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        var result = decoder.TryDecode(responseData, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact]
    public void Roundtrip_PostWithBody_ContentLengthRoundtrips()
    {
        const string requestBody = "username=test&password=secret";

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/login")
        {
            Content = new StringContent(requestBody, Encoding.ASCII, "application/x-www-form-urlencoded")
        };
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encodedRequest = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.Contains($"Content-Length: {Encoding.ASCII.GetByteCount(requestBody)}", encodedRequest);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {requestBody.Length}\r\nContent-Type: text/plain",
            requestBody);

        decoder.TryDecode(responseData, out var response);
        var responseBody = response!.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        Assert.Equal(requestBody, responseBody);
    }

    [Fact]
    public void Roundtrip_EncoderForbiddenHeaders_NotInDecodedResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.False(response!.Headers.Contains("Connection"));
        Assert.False(response.Headers.Contains("Transfer-Encoding"));
    }

    [Fact]
    public async Task Roundtrip_BinaryBody_PreservedThroughEncodeAndDecode()
    {
        var originalBody = new byte[256];
        for (var i = 0; i < 256; i++) originalBody[i] = (byte)i;

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload")
        {
            Content = new ByteArrayContent(originalBody)
        };
        var encBuffer = new Memory<byte>(new byte[16384]);
        var written = Http10Encoder.Encode(request, ref encBuffer);

        var encodedStr = Encoding.ASCII.GetString(encBuffer.Span[..20]);
        Assert.Contains("POST", encodedStr);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {originalBody.Length}", originalBody);

        decoder.TryDecode(responseData, out var response);
        var decodedBody = await response!.Content.ReadAsByteArrayAsync();

        Assert.Equal(originalBody, decodedBody);
    }

    [Fact]
    public void Roundtrip_CustomHeadersSurviveEncoderAndDecoder()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "abc-123-def");

        var encBuffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref encBuffer);
        var encodedRequest = Encoding.ASCII.GetString(encBuffer.Span[..written]);

        Assert.Contains("X-Correlation-Id: abc-123-def", encodedRequest);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Correlation-Id: abc-123-def\r\nContent-Length: 0");

        decoder.TryDecode(responseData, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Correlation-Id", out var values));
        Assert.Contains("abc-123-def", values);
    }

    [Fact]
    public async Task Roundtrip_FragmentedResponse_AssembledCorrectly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/chunked");
        var encBuffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref encBuffer);

        var decoder = new Http10Decoder();
        var fullResponse = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 13\r\nX-Request: ok\r\n\r\nHello, World!");
        var fullArray = fullResponse.ToArray();

        var fragmentSize = fullArray.Length / 4;
        HttpResponseMessage? finalResponse = null;

        for (var i = 0; i < 4; i++)
        {
            var start = i * fragmentSize;
            var length = i == 3 ? fullArray.Length - start : fragmentSize;
            var fragment = new ReadOnlyMemory<byte>(fullArray, start, length);

            if (decoder.TryDecode(fragment, out finalResponse))
                break;
        }

        Assert.NotNull(finalResponse);
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
        var body = await finalResponse.Content.ReadAsStringAsync();
        Assert.Equal("Hello, World!", body);
    }

    [Fact]
    public void Roundtrip_HeadRequest_ResponseHasNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var encBuffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref encBuffer);
        var encodedRequest = Encoding.ASCII.GetString(encBuffer.Span[..written]);

        Assert.StartsWith("HEAD /resource HTTP/1.0", encodedRequest);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Length: 1024\r\nContent-Type: application/octet-stream");

        decoder.TryDecode(responseData, out _);
        decoder.TryDecodeEof(out var response);

        Assert.NotNull(response);
    }

    // ── RT-10-008 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-008: HTTP/1.0 PUT → 200 OK round-trip")]
    public void Should_Return200_When_PutRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource/1")
        {
            Content = new StringContent("{\"name\":\"Alice\"}", Encoding.UTF8, "application/json")
        };
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.StartsWith("PUT /resource/1 HTTP/1.0\r\n", encoded);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    // ── RT-10-009 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-009: HTTP/1.0 DELETE → 200 OK round-trip")]
    public void Should_Return200_When_DeleteRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource/5");
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.StartsWith("DELETE /resource/5 HTTP/1.0\r\n", encoded);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    // ── RT-10-010 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-010: HTTP/1.0 OPTIONS → 200 with Allow header")]
    public void Should_ReturnAllowHeader_When_OptionsRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "http://example.com/");
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.StartsWith("OPTIONS / HTTP/1.0\r\n", encoded);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Allow: GET, POST, OPTIONS\r\nContent-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Allow", out var vals));
        Assert.Contains("GET", string.Join(",", vals));
    }

    // ── RT-10-011 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-011: HTTP/1.0 PATCH → 200 OK round-trip")]
    public async Task Should_Return200_When_PatchRoundTrip()
    {
        const string patch = "[{\"op\":\"add\",\"path\":\"/x\",\"value\":1}]";
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), "http://example.com/item/2")
        {
            Content = new StringContent(patch, Encoding.ASCII, "application/json-patch+json")
        };
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.StartsWith("PATCH /item/2 HTTP/1.0\r\n", encoded);

        const string responseBody = "updated";
        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {responseBody.Length}", responseBody);
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal(responseBody, await response.Content.ReadAsStringAsync());
    }

    // ── RT-10-012 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-012: HTTP/1.0 GET with query string → URI correctly encoded")]
    public void Should_IncludeQueryString_When_GetWithQueryStringRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "http://example.com/search?q=hello+world&page=2");
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.Contains("/search?q=hello+world&page=2 HTTP/1.0", encoded);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    // ── RT-10-013 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-013: HTTP/1.0 POST → 201 Created response")]
    public void Should_Return201Created_When_PostRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/items")
        {
            Content = new StringContent("{}", Encoding.ASCII, "application/json")
        };
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 201 Created",
            "Location: /items/42\r\nContent-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.Created, response!.StatusCode);
        Assert.Equal("Created", response.ReasonPhrase);
    }

    // ── RT-10-014 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-014: HTTP/1.0 DELETE → 204 No Content (body empty per RFC 1945)")]
    public async Task Should_ReturnEmptyBody_When_204NoContentResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource/3");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 204 No Content", "");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.NoContent, response!.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    // ── RT-10-015 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-015: HTTP/1.0 GET → 304 Not Modified (body empty per RFC 1945)")]
    public async Task Should_ReturnEmptyBody_When_304NotModifiedResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"etag-value\"");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 304 Not Modified", "ETag: \"etag-value\"");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.NotModified, response!.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    // ── RT-10-016 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-016: HTTP/1.0 GET → 301 Moved Permanently with Location")]
    public void Should_Return301WithLocation_When_GetRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 301 Moved Permanently",
            "Location: http://example.com/new\r\nContent-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.MovedPermanently, response!.StatusCode);
        Assert.True(response.Headers.TryGetValues("Location", out var loc));
        Assert.Contains("new", loc.Single());
    }

    // ── RT-10-017 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-017: HTTP/1.0 GET → 302 Found with Location")]
    public void Should_Return302Found_When_GetRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/temp");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 302 Found",
            "Location: http://example.com/final\r\nContent-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.Found, response!.StatusCode);
        Assert.True(response.Headers.TryGetValues("Location", out var loc));
        Assert.Contains("final", loc.Single());
    }

    // ── RT-10-018 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-018: HTTP/1.0 GET → 400 Bad Request with body")]
    public async Task Should_Return400_When_BadRequestResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/endpoint");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        const string body = "Bad Request: invalid parameter";
        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 400 Bad Request",
            $"Content-Length: {body.Length}", body);
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.BadRequest, response!.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
    }

    // ── RT-10-019 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-019: HTTP/1.0 GET → 401 Unauthorized with WWW-Authenticate")]
    public void Should_Return401WithWwwAuthenticate_When_GetRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/secure");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 401 Unauthorized",
            "WWW-Authenticate: Basic realm=\"protected\"\r\nContent-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.Unauthorized, response!.StatusCode);
        Assert.True(response.Headers.TryGetValues("WWW-Authenticate", out var vals));
        Assert.Contains("Basic", vals.Single());
    }

    // ── RT-10-020 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-020: HTTP/1.0 GET → 403 Forbidden")]
    public void Should_Return403_When_ForbiddenResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/admin");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 403 Forbidden", "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.Forbidden, response!.StatusCode);
        Assert.Equal("Forbidden", response.ReasonPhrase);
    }

    // ── RT-10-021 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-021: HTTP/1.0 GET → 500 Internal Server Error")]
    public void Should_Return500_When_ServerErrorResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/broken");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 500 Internal Server Error",
            "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
    }

    // ── RT-10-022 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-022: HTTP/1.0 GET → 503 Service Unavailable with Retry-After")]
    public void Should_Return503WithRetryAfter_When_ServiceUnavailableResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/status");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 503 Service Unavailable",
            "Retry-After: 30\r\nContent-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response!.StatusCode);
        Assert.True(response.Headers.TryGetValues("Retry-After", out var vals));
        Assert.Equal("30", vals.Single());
    }

    // ── RT-10-023 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-023: HTTP/1.0 multi-word reason phrase preserved in response")]
    public void Should_PreserveMultiWordReasonPhrase_When_ResponseDecoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 422 Unprocessable Entity",
            "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal((HttpStatusCode)422, response!.StatusCode);
        Assert.Equal("Unprocessable Entity", response.ReasonPhrase);
    }

    // ── RT-10-024 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-024: Server header preserved in decoded response")]
    public void Should_PreserveServerHeader_When_ResponseDecoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "Server: Apache/2.4.41\r\nContent-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.True(response!.Headers.TryGetValues("Server", out var vals));
        Assert.Equal("Apache/2.4.41", vals.Single());
    }

    // ── RT-10-025 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-025: Date header preserved in decoded response")]
    public void Should_PreserveDateHeader_When_ResponseDecoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "Date: Mon, 01 Jan 2024 00:00:00 GMT\r\nContent-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.True(response!.Headers.TryGetValues("Date", out var vals));
        Assert.Contains("2024", vals.Single());
    }

    // ── RT-10-026 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-026: ETag header preserved in decoded response")]
    public void Should_PreserveETagHeader_When_ResponseDecoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "ETag: \"abc-def-123\"\r\nContent-Length: 2", "ok");
        decoder.TryDecode(responseData, out var response);

        Assert.True(response!.Headers.TryGetValues("ETag", out var vals));
        Assert.Equal("\"abc-def-123\"", vals.Single());
    }

    // ── RT-10-027 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-027: Last-Modified preserved as content header in response")]
    public void Should_PreserveLastModified_When_ResponseDecoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        const string lastModified = "Tue, 15 Nov 1994 08:12:31 GMT";
        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            $"Last-Modified: {lastModified}\r\nContent-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.True(response!.Content.Headers.TryGetValues("Last-Modified", out var vals));
        Assert.Contains("1994", vals.Single());
    }

    // ── RT-10-028 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-028: Content-Type text/plain preserved in response")]
    public void Should_PreserveContentTypePlain_When_ResponseDecoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/text");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Type: text/plain\r\nContent-Length: 5", "hello");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal("text/plain", response!.Content.Headers.ContentType!.MediaType);
    }

    // ── RT-10-029 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-029: Content-Type with charset preserved in response")]
    public void Should_PreserveContentTypeWithCharset_When_ResponseDecoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/html");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Type: text/html; charset=utf-8\r\nContent-Length: 6", "<html>");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal("text/html", response!.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType!.CharSet);
    }

    // ── RT-10-030 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-030: Multiple custom X- headers all preserved")]
    public void Should_PreserveMultipleCustomHeaders_When_ResponseDecoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "req-001");
        request.Headers.TryAddWithoutValidation("X-Source", "test-suite");
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        // .NET normalizes some header names — check case-insensitively
        Assert.Contains("req-001", encoded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-Source", encoded, StringComparison.OrdinalIgnoreCase);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Response-Id: resp-001\r\nX-Server: backend-1\r\nX-Trace: trace-abc\r\nContent-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Response-Id", out var v1));
        Assert.Equal("resp-001", v1.Single());
        Assert.True(response.Headers.TryGetValues("X-Server", out var v2));
        Assert.Equal("backend-1", v2.Single());
        Assert.True(response.Headers.TryGetValues("X-Trace", out var v3));
        Assert.Equal("trace-abc", v3.Single());
    }

    // ── RT-10-031 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-031: Cache-Control header preserved in response")]
    public void Should_PreserveCacheControl_When_ResponseDecoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/asset.js");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "Cache-Control: public, max-age=3600\r\nContent-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.True(response!.Headers.TryGetValues("Cache-Control", out var vals));
        Assert.Contains("max-age=3600", vals.Single());
    }

    // ── RT-10-032 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-032: POST with no content emits Content-Length: 0")]
    public void Should_EmitContentLengthZero_When_PostWithNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/trigger");
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.Contains("Content-Length: 0", encoded);
        Assert.DoesNotContain("Content-Type:", encoded);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    // ── RT-10-033 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-033: PUT with JSON body encoded and response body preserved")]
    public async Task Should_PreserveJsonBody_When_PutRoundTrip()
    {
        const string requestJson = "{\"id\":42,\"name\":\"Bob\",\"active\":true}";
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/users/42")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(requestJson)}", encoded);
        Assert.Contains("Content-Type: application/json", encoded);
        Assert.Contains(requestJson, encoded);

        const string responseJson = "{\"updated\":true}";
        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Type: application/json\r\nContent-Length: {responseJson.Length}",
            responseJson);
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal(responseJson, await response.Content.ReadAsStringAsync());
    }

    // ── RT-10-034 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-034: POST with form-encoded body round-trip")]
    public async Task Should_EncodeFormData_When_PostFormRoundTrip()
    {
        const string formData = "username=alice&password=secret&remember=true";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/login")
        {
            Content = new StringContent(formData, Encoding.ASCII,
                "application/x-www-form-urlencoded")
        };
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.Contains("Content-Type: application/x-www-form-urlencoded", encoded);
        Assert.Contains(formData, encoded);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Type: text/plain\r\nContent-Length: 2", "OK");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal("OK", await response.Content.ReadAsStringAsync());
    }

    // ── RT-10-035 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-035: Large response body (64 KB) preserved through decode")]
    public async Task Should_PreserveLargeBody_When_64KBResponseDecoded()
    {
        const int bodySize = 65536;
        var body = new byte[bodySize];
        for (var i = 0; i < bodySize; i++) { body[i] = (byte)(i % 256); }

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/large");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodySize}", body);
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsByteArrayAsync());
    }

    // ── RT-10-036 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-036: UTF-8 multi-byte body bytes preserved through decode")]
    public async Task Should_PreserveUtf8BodyBytes_When_ResponseDecoded()
    {
        const string body = "Hello Wörld! ñ€£";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/intl");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Type: text/plain; charset=utf-8\r\nContent-Length: {bodyBytes.Length}",
            bodyBytes);
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal(bodyBytes, await response.Content.ReadAsByteArrayAsync());
    }

    // ── RT-10-037 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-037: Two-fragment TCP delivery assembles correctly")]
    public async Task Should_AssembleResponse_When_TwoFragmentsDelivered()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var fullResponse = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        var fullArray = fullResponse.ToArray();

        var half = fullArray.Length / 2;
        var frag1 = new ReadOnlyMemory<byte>(fullArray, 0, half);
        var frag2 = new ReadOnlyMemory<byte>(fullArray, half, fullArray.Length - half);

        var result1 = decoder.TryDecode(frag1, out _);
        Assert.False(result1);

        var result2 = decoder.TryDecode(frag2, out var response);
        Assert.True(result2);
        Assert.Equal("hello", await response!.Content.ReadAsStringAsync());
    }

    // ── RT-10-038 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-038: Single-byte fragment delivery assembles correctly")]
    public async Task Should_AssembleResponse_When_SingleByteFragments()
    {
        var decoder = new Http10Decoder();
        var fullResponse = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nabc");
        var fullArray = fullResponse.ToArray();

        HttpResponseMessage? finalResponse = null;
        for (var i = 0; i < fullArray.Length; i++)
        {
            var fragment = new ReadOnlyMemory<byte>(fullArray, i, 1);
            if (decoder.TryDecode(fragment, out finalResponse))
            {
                break;
            }
        }

        Assert.NotNull(finalResponse);
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
        Assert.Equal("abc", await finalResponse.Content.ReadAsStringAsync());
    }

    // ── RT-10-039 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-039: Fragment boundary mid-header assembles correctly")]
    public async Task Should_AssembleResponse_When_FragmentBoundaryInHeader()
    {
        var decoder = new Http10Decoder();
        var frag1 = Bytes("HTTP/1.0 200 OK\r\nContent-Len");
        var frag2 = Bytes("gth: 4\r\n\r\ntest");

        var r1 = decoder.TryDecode(frag1, out _);
        Assert.False(r1);

        var r2 = decoder.TryDecode(frag2, out var response);
        Assert.True(r2);
        Assert.Equal("test", await response!.Content.ReadAsStringAsync());
    }

    // ── RT-10-040 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-040: Fragment boundary in body assembles correctly")]
    public async Task Should_AssembleResponse_When_FragmentBoundaryInBody()
    {
        var decoder = new Http10Decoder();
        var headerAndPartialBody = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 10\r\n\r\nhello");
        var remainingBody = Bytes("world");

        var r1 = decoder.TryDecode(headerAndPartialBody, out _);
        Assert.False(r1);

        var r2 = decoder.TryDecode(remainingBody, out var response);
        Assert.True(r2);
        Assert.Equal("helloworld", await response!.Content.ReadAsStringAsync());
    }

    // ── RT-10-041 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-041: Decoder.Reset() allows reuse for a new response")]
    public async Task Should_DecodeNewResponse_When_DecoderReset()
    {
        var decoder = new Http10Decoder();

        var resp1 = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 5", "first");
        decoder.TryDecode(resp1, out var response1);
        Assert.Equal("first", await response1!.Content.ReadAsStringAsync());

        decoder.Reset();

        var resp2 = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 6", "second");
        decoder.TryDecode(resp2, out var response2);
        Assert.Equal("second", await response2!.Content.ReadAsStringAsync());
    }

    // ── RT-10-042 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-042: Two independent decoders decode responses independently")]
    public async Task Should_DecodeSeparately_When_TwoIndependentDecoders()
    {
        var decoder1 = new Http10Decoder();
        var decoder2 = new Http10Decoder();

        var resp1 = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 3", "aaa");
        var resp2 = BuildRawResponse("HTTP/1.0 404 Not Found", "Content-Length: 3", "bbb");

        decoder1.TryDecode(resp1, out var response1);
        decoder2.TryDecode(resp2, out var response2);

        Assert.Equal(HttpStatusCode.OK, response1!.StatusCode);
        Assert.Equal("aaa", await response1.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, response2!.StatusCode);
        Assert.Equal("bbb", await response2.Content.ReadAsStringAsync());
    }

    // ── RT-10-043 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-043: Response without Content-Length decoded from available bytes")]
    public async Task Should_DecodeBody_When_NoContentLengthAndBodyPresent()
    {
        var decoder = new Http10Decoder();
        var fullData = Bytes("HTTP/1.0 200 OK\r\nX-Info: test\r\n\r\nfull body text");
        decoder.TryDecode(fullData, out var response);

        Assert.NotNull(response);
        Assert.Equal("full body text", await response!.Content.ReadAsStringAsync());
    }

    // ── RT-10-044 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-044: TryDecodeEof returns response from buffered partial body")]
    public async Task Should_ReturnResponse_When_TryDecodeEofCalledWithBufferedData()
    {
        var decoder = new Http10Decoder();

        // Content-Length: 100 but only 5 body bytes arrive, then connection closes
        var partialResponse = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nhello");
        var r1 = decoder.TryDecode(partialResponse, out _);
        Assert.False(r1);

        var r2 = decoder.TryDecodeEof(out var response);
        Assert.True(r2);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
    }

    // ── RT-10-045 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-045: Absolute-form URI encoding round-trip")]
    public void Should_UseAbsoluteUri_When_AbsoluteFormRequested()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "http://proxy.example.com/resource?key=val");
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer, absoluteForm: true);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.StartsWith("GET http://proxy.example.com/resource?key=val HTTP/1.0\r\n", encoded);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    // ── RT-10-046 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-046: Host header stripped from HTTP/1.0 request (RFC 1945)")]
    public void Should_StripHostHeader_When_Http10RequestEncoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.TryAddWithoutValidation("Host", "example.com");

        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.DoesNotContain("Host:", encoded);
    }

    // ── RT-10-047 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-047: Transfer-Encoding header stripped from HTTP/1.0 request")]
    public void Should_StripTransferEncoding_When_Http10RequestEncoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new StringContent("payload", Encoding.ASCII)
        };
        request.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked");

        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.DoesNotContain("Transfer-Encoding:", encoded);
    }

    // ── RT-10-048 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-048: Decoded HTTP/1.0 response has Version 1.0")]
    public void Should_SetVersion10_When_Http10ResponseDecoded()
    {
        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(new Version(1, 0), response!.Version);
    }

    // ── RT-10-049 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-049: Very long header value (4096 chars) preserved in request")]
    public void Should_PreserveLongHeaderValue_When_VeryLongHeaderPresent()
    {
        var longValue = new string('A', 4096);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Long-Value", longValue);

        var buffer = new Memory<byte>(new byte[16384]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.Contains($"X-Long-Value: {longValue}", encoded);
    }

    // ── RT-10-050 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-10-050: Deep path /api/v2/items preserved in GET request")]
    public void Should_PreserveDeepPath_When_GetWithDeepPathRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "http://example.com/api/v2/items");
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encoded = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.StartsWith("GET /api/v2/items HTTP/1.0\r\n", encoded);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }
}