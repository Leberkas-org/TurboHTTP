using System.Buffers;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http11EncoderTests
{
    [Fact]
    public void Get_ProducesCorrectRequestLine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/index.html");
        var result = Encode(request);
        Assert.StartsWith("GET /index.html HTTP/1.1\r\n", result);
    }

    [Fact]
    public void Get_ContainsHostHeader_Port80_NoPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:80/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact]
    public void Get_ContainsHostHeader_Port443_NoPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/secure");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact]
    public void Get_NonStandardPort_IncludesPortInHost()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/");
        var result = Encode(request);
        Assert.Contains("Host: example.com:8080\r\n", result);
    }

    [Fact]
    public void Get_DefaultConnectionHeader_IsKeepAlive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact]
    public void Get_ExplicitConnectionClose_IsPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Connection", "close" } }
        };
        var result = Encode(request);
        Assert.Contains("Connection: close\r\n", result);
        Assert.DoesNotContain("Connection: keep-alive", result);
    }

    [Fact]
    public void Get_EndsWithBlankLine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.EndsWith("\r\n\r\n", result);
    }

    [Fact]
    public void Get_WithQueryParams_EncodesQueryString()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=hello+world&lang=de");
        var result = Encode(request);
        Assert.Contains("/search?q=hello+world&lang=de", result);
    }

    [Fact]
    public void Post_WithJsonBody_SetsContentTypeAndLength()
    {
        const string json = """{"name":"test"}""";
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/users")
        {
            Content = content
        };
        var result = Encode(request);

        Assert.Contains("POST /users HTTP/1.1\r\n", result);
        Assert.Contains("Content-Type: application/json", result);
        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(json)}", result);
    }

    [Fact]
    public void Post_WithJsonBody_BodyAppearsAfterBlankLine()
    {
        const string json = """{"x":1}""";
        var content = new StringContent(json);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/data")
        {
            Content = content
        };
        var result = Encode(request);

        var separatorIdx = result.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIdx > 0);
        Assert.Equal(json, result[(separatorIdx + 4)..]);
    }

    [Fact]
    public void Post_BufferTooSmallForBody_Throws()
    {
        var content = new ByteArrayContent(new byte[3000]);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/data")
        {
            Content = content
        };
        var buffer = new Memory<byte>(new byte[200]);
        Assert.Throws<ArgumentException>(() => Http11Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void Encode_BufferTooSmallForHeaders_Throws()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var buffer = new Memory<byte>(new byte[1]);
        Assert.Throws<ArgumentException>(() => Http11Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void BearerToken_SetsAuthorizationHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected")
        {
            Headers = { { "Authorization", "Bearer my-secret-token" } }
        };
        var result = Encode(request);
        Assert.Contains("Authorization: Bearer my-secret-token\r\n", result);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Phase 3: RFC 9112 / RFC 7230 HTTP/1.1 Encoder Tests
    // ════════════════════════════════════════════════════════════════════════════

    // ── Request-Line (RFC 7230 §3.1.1) ─────────────────────────────────────────

    [Fact(DisplayName = "7230-enc-001: Request-line uses HTTP/1.1")]
    public void Test_7230_enc_001_RequestLine_UsesHttp11()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("GET / HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "7230-3.1.1-002: Lowercase method rejected by HTTP/1.1 encoder")]
    public void Test_7230_3_1_1_002_Lowercase_Method_Rejected()
    {
        var request = new HttpRequestMessage(new HttpMethod("get"), "https://example.com/");
        var buffer = new Memory<byte>(new byte[4096]);
        Assert.Throws<ArgumentException>(() => Http11Encoder.Encode(request, ref buffer));
    }

    [Fact(DisplayName = "7230-3.1.1-004: Every request-line ends with CRLF")]
    public void Test_7230_3_1_1_004_RequestLine_Ends_With_CRLF()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");
        var result = Encode(request);
        Assert.Contains("GET /test HTTP/1.1\r\n", result);
    }

    [Theory(DisplayName = "enc3-m-001: All HTTP methods produce correct request-line [{method}]")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("CONNECT")]
    public void Test_enc3_m_001_All_Methods(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/resource");
        var result = Encode(request);
        Assert.StartsWith($"{method} /resource HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "enc3-uri-001: OPTIONS * HTTP/1.1 encoded correctly")]
    public void Test_enc3_uri_001_OPTIONS_Star()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "https://example.com/*");
        var result = Encode(request);
        Assert.Contains("OPTIONS * HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "enc3-uri-002: Absolute-URI preserved for proxy request")]
    public void Test_enc3_uri_002_Absolute_URI_For_Proxy()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8443/path?query=value");
        var result = EncodeAbsolute(request);
        Assert.Contains("GET https://example.com:8443/path?query=value HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "enc3-uri-003: Missing path normalized to /")]
    public void Test_enc3_uri_003_Missing_Path_Normalized()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var result = Encode(request);
        Assert.Contains("GET / HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "enc3-uri-004: Query string preserved verbatim")]
    public void Test_enc3_uri_004_Query_String_Preserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=hello+world&lang=en");
        var result = Encode(request);
        Assert.Contains("GET /search?q=hello+world&lang=en HTTP/1.1\r\n", result);
    }

    [Fact(DisplayName = "enc3-uri-005: Fragment stripped from request-target")]
    public void Test_enc3_uri_005_Fragment_Stripped()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page#section");
        var result = Encode(request);
        Assert.Contains("GET /page HTTP/1.1\r\n", result);
        Assert.DoesNotContain("#section", result);
    }

    [Fact(DisplayName = "enc3-uri-006: Existing percent-encoding not re-encoded")]
    public void Test_enc3_uri_006_Percent_Encoding_Not_Re_Encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path%20with%20spaces");
        var result = Encode(request);
        Assert.Contains("GET /path%20with%20spaces HTTP/1.1\r\n", result);
    }

    // ── Mandatory Host Header ───────────────────────────────────────────────────

    [Fact(DisplayName = "RFC 9112 §5.4: Host header mandatory in HTTP/1.1")]
    public void Test_9112_enc_001_Host_Always_Present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
    }

    [Fact(DisplayName = "RFC 9112 §5.4: Host header emitted exactly once")]
    public void Test_9112_enc_002_Host_Emitted_Once()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        var count = System.Text.RegularExpressions.Regex.Matches(result, "Host:").Count;
        Assert.Equal(1, count);
    }

    [Fact(DisplayName = "enc3-host-001: Host with non-standard port includes port")]
    public void Test_enc3_host_001_Non_Standard_Port()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/");
        var result = Encode(request);
        Assert.Contains("Host: example.com:8080\r\n", result);
    }

    [Fact(DisplayName = "enc3-host-002: IPv6 host literal bracketed correctly")]
    public void Test_enc3_host_002_IPv6_Bracketed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://[::1]:8080/");
        var result = Encode(request);
        Assert.Contains("Host: [::1]:8080\r\n", result);
    }

    [Fact(DisplayName = "enc3-host-003: Default port 80 omitted from Host header")]
    public void Test_enc3_host_003_Default_Port_Omitted()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:80/");
        var result = Encode(request);
        Assert.Contains("Host: example.com\r\n", result);
        Assert.DoesNotContain("Host: example.com:80", result);
    }

    // ── Header Encoding (RFC 7230 §3.2) ────────────────────────────────────────

    [Fact(DisplayName = "7230-3.2-001: Header field format is Name: SP value CRLF")]
    public void Test_7230_3_2_001_Header_Format()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Custom", "test-value" } }
        };
        var result = Encode(request);
        Assert.Contains("X-Custom: test-value\r\n", result);
    }

    [Fact(DisplayName = "7230-3.2-002: No spurious whitespace added to header values")]
    public void Test_7230_3_2_002_No_Spurious_Whitespace()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "X-Test", "value" } }
        };
        var result = Encode(request);
        Assert.Contains("X-Test: value\r\n", result);
        Assert.DoesNotContain("X-Test:  value", result);
    }

    [Fact(DisplayName = "7230-3.2-007: Header name casing preserved in output")]
    public void Test_7230_3_2_007_Header_Name_Casing_Preserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("X-Custom-Header", "value");
        var result = Encode(request);
        Assert.Contains("X-Custom-Header: value\r\n", result);
    }

    [Fact(DisplayName = "enc3-hdr-001: NUL byte in header value throws exception")]
    public void Test_enc3_hdr_001_NUL_Byte_Rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("X-Bad", "value\0bad");
        var buffer = new Memory<byte>(new byte[4096]);
        Assert.Throws<ArgumentException>(() => Http11Encoder.Encode(request, ref buffer));
    }

    [Fact(DisplayName = "enc3-hdr-002: Content-Type with charset parameter preserved")]
    public void Test_enc3_hdr_002_Content_Type_With_Charset()
    {
        var content = new StringContent("test", Encoding.UTF8, "text/html");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        Assert.Contains("Content-Type: text/html; charset=utf-8\r\n", result);
    }

    [Fact(DisplayName = "enc3-hdr-003: All custom headers appear in output")]
    public void Test_enc3_hdr_003_Custom_Headers_Appear()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers =
            {
                { "X-First", "value1" },
                { "X-Second", "value2" },
                { "X-Third", "value3" }
            }
        };
        var result = Encode(request);
        Assert.Contains("X-First: value1\r\n", result);
        Assert.Contains("X-Second: value2\r\n", result);
        Assert.Contains("X-Third: value3\r\n", result);
    }

    [Fact(DisplayName = "enc3-hdr-004: Accept-Encoding gzip,deflate encoded")]
    public void Test_enc3_hdr_004_Accept_Encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        var result = Encode(request);
        Assert.Contains("Accept-Encoding: gzip, deflate\r\n", result);
    }

    [Fact(DisplayName = "enc3-hdr-005: Authorization header preserved verbatim")]
    public void Test_enc3_hdr_005_Authorization_Preserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Authorization", "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" } }
        };
        var result = Encode(request);
        Assert.Contains("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9\r\n", result);
    }

    // ── Connection Management ───────────────────────────────────────────────────

    [Fact(DisplayName = "7230-enc-003: Connection keep-alive default in HTTP/1.1")]
    public void Test_7230_enc_003_Default_Keep_Alive()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.Contains("Connection: keep-alive\r\n", result);
    }

    [Fact(DisplayName = "7230-enc-004: Connection close encoded when set")]
    public void Test_7230_enc_004_Connection_Close()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers = { { "Connection", "close" } }
        };
        var result = Encode(request);
        Assert.Contains("Connection: close\r\n", result);
        Assert.DoesNotContain("keep-alive", result);
    }

    [Fact(DisplayName = "7230-6.1-005: Multiple Connection tokens encoded")]
    public void Test_7230_6_1_005_Multiple_Connection_Tokens()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Connection.Add("upgrade");
        var result = Encode(request);
        Assert.Contains("Connection: upgrade, keep-alive\r\n", result);
    }

    [Fact(DisplayName = "RFC 9112: Connection-specific headers stripped")]
    public void Test_9112_enc_003_Connection_Specific_Headers_Stripped()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        var result = Encode(request);
        Assert.DoesNotContain("TE:", result);
        Assert.DoesNotContain("Keep-Alive:", result);
        Assert.DoesNotContain("Upgrade:", result);
    }

    // ── Body Encoding ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "7230-enc-006: No Content-Length for bodyless GET")]
    public void Test_7230_enc_006_No_Content_Length_For_GET()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var result = Encode(request);
        Assert.DoesNotContain("Content-Length:", result);
    }

    [Fact(DisplayName = "7230-enc-008: Content-Length set for POST body")]
    public void Test_7230_enc_008_Content_Length_For_POST()
    {
        var content = new StringContent("test data");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        Assert.Contains("Content-Length:", result);
    }

    [Theory(DisplayName = "enc3-body-001: {method} with body gets Content-Length [{method}]")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public void Test_enc3_body_001_Methods_With_Body_Get_Content_Length(string method)
    {
        var content = new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5 });
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        Assert.Contains("Content-Length: 5\r\n", result);
    }

    [Theory(DisplayName = "enc3-body-002: {method} without body omits Content-Length [{method}]")]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("DELETE")]
    public void Test_enc3_body_002_Methods_Without_Body_Omit_Content_Length(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/");
        var result = Encode(request);
        Assert.DoesNotContain("Content-Length:", result);
    }

    [Fact(DisplayName = "enc3-body-003: Empty line separates headers from body")]
    public void Test_enc3_body_003_Empty_Line_Separator()
    {
        var content = new StringContent("body content");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        var result = Encode(request);
        var separatorIdx = result.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIdx > 0, "Empty line separator not found");
        Assert.StartsWith("body content", result[(separatorIdx + 4)..]);
    }

    [Fact(DisplayName = "enc3-body-004: Binary body with null bytes preserved")]
    public void Test_enc3_body_004_Binary_Body_Preserved()
    {
        var binaryData = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x00, 0x03 };
        var content = new ByteArrayContent(binaryData);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var written = Http11Encoder.Encode(request, ref buffer);
        var bytes = buffer.Span[..(int)written].ToArray();

        // Find body start (after \r\n\r\n)
        var bodyStart = -1;
        for (var i = 0; i < bytes.Length - 3; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
            {
                bodyStart = i + 4;
                break;
            }
        }
        Assert.True(bodyStart > 0);
        var body = bytes[bodyStart..(bodyStart + binaryData.Length)];
        Assert.Equal(binaryData, body);
    }

    [Fact(DisplayName = "7230-enc-009: Chunked Transfer-Encoding for streamed body")]
    public void Test_7230_enc_009_Chunked_Transfer_Encoding()
    {
        var content = new StringContent("Hello World");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = content
        };
        request.Headers.TransferEncodingChunked = true;

        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var written = Http11Encoder.Encode(request, ref buffer);
        var bytes = buffer.Span[..(int)written].ToArray();
        var result = Encoding.ASCII.GetString(bytes);

        // Verify Transfer-Encoding: chunked is present
        Assert.Contains("Transfer-Encoding: chunked\r\n", result);

        // Find body start (after \r\n\r\n)
        var separatorIdx = result.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIdx > 0);
        var bodyPart = result[(separatorIdx + 4)..];

        // Verify chunked encoding format: size in hex + CRLF + data + CRLF
        // "Hello World" = 11 bytes = 0xb in hex
        Assert.StartsWith("b\r\nHello World\r\n", bodyPart);
    }

    [Fact(DisplayName = "enc3-body-005: Chunked body terminated with final 0-chunk")]
    public void Test_enc3_body_005_Chunked_Body_Terminator()
    {
        var content = new StringContent("Test");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        request.Headers.TransferEncodingChunked = true;

        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var written = Http11Encoder.Encode(request, ref buffer);
        var bytes = buffer.Span[..(int)written].ToArray();
        var result = Encoding.ASCII.GetString(bytes);

        // Verify the message ends with the final chunk: 0\r\n\r\n
        Assert.EndsWith("0\r\n\r\n", result);
    }

    [Fact(DisplayName = "enc3-body-006: Content-Length absent when Transfer-Encoding is chunked")]
    public void Test_enc3_body_006_No_Content_Length_When_Chunked()
    {
        var content = new StringContent("Some data here");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = content
        };
        request.Headers.TransferEncodingChunked = true;

        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var written = Http11Encoder.Encode(request, ref buffer);
        var bytes = buffer.Span[..(int)written].ToArray();
        var result = Encoding.ASCII.GetString(bytes);

        // RFC 7230 Section 3.3.2: Content-Length MUST NOT be sent when Transfer-Encoding is present
        Assert.DoesNotContain("Content-Length:", result);
        Assert.Contains("Transfer-Encoding: chunked\r\n", result);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Phase 4b: RFC 7233 — Range Requests (Encoder)
    // ════════════════════════════════════════════════════════════════════════════

    // ── Range Header Encoding (RFC 7233 §2.1) ───────────────────────────────────

    [Fact(DisplayName = "7233-2.1-001: Range: bytes=0-499 encoded")]
    public void Test_7233_2_1_001_Range_Bytes_Encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 499);
        var result = Encode(request);
        Assert.Contains("Range: bytes=0-499\r\n", result);
    }

    [Fact(DisplayName = "7233-2.1-002: Range: bytes=-500 suffix encoded")]
    public void Test_7233_2_1_002_Range_Suffix_Encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(null, 500);
        var result = Encode(request);
        Assert.Contains("Range: bytes=-500\r\n", result);
    }

    [Fact(DisplayName = "7233-2.1-003: Range: bytes=500- open range encoded")]
    public void Test_7233_2_1_003_Range_OpenEnded_Encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(500, null);
        var result = Encode(request);
        Assert.Contains("Range: bytes=500-\r\n", result);
    }

    [Fact(DisplayName = "7233-2.1-004: Multi-range bytes=0-499,1000-1499 encoded")]
    public void Test_7233_2_1_004_Range_MultiRange_Encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        var range = new System.Net.Http.Headers.RangeHeaderValue();
        range.Ranges.Add(new System.Net.Http.Headers.RangeItemHeaderValue(0, 499));
        range.Ranges.Add(new System.Net.Http.Headers.RangeItemHeaderValue(1000, 1499));
        request.Headers.Range = range;
        var result = Encode(request);
        Assert.Contains("Range: bytes=", result);
        Assert.Contains("0-499", result);
        Assert.Contains("1000-1499", result);
    }

    [Fact(DisplayName = "7233-2.1-005: Invalid range bytes=abc-xyz rejected")]
    public void Test_7233_2_1_005_Invalid_Range_Rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "bytes=abc-xyz");
        var buffer = new Memory<byte>(new byte[4096]);
        Assert.Throws<ArgumentException>(() => Http11Encoder.Encode(request, ref buffer));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ════════════════════════════════════════════════════════════════════════════

    private static string Encode(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var written = Http11Encoder.Encode(request, ref buffer);
        return Encoding.ASCII.GetString(buffer.Span[..(int)written]);
    }

    private static string EncodeAbsolute(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var written = Http11Encoder.Encode(request, ref buffer, absoluteForm: true);
        return Encoding.ASCII.GetString(buffer.Span[..(int)written]);
    }
}