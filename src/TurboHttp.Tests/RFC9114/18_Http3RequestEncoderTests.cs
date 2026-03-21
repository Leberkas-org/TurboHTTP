using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Protocol.RFC9204;
using Xunit;

namespace TurboHttp.Tests.RFC9114;

/// <summary>
/// RFC 9114 §4.1, §4.3.1 — Http3RequestEncoder unit tests.
/// Verifies HttpRequestMessage → HEADERS + DATA frame encoding with QPACK compression
/// and correct pseudo-header construction.
/// </summary>
public sealed class Http3RequestEncoderTests
{
    // ───────────────────────── Frame Structure ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-enc-001: GET request produces single HEADERS frame")]
    public void Get_request_produces_single_headers_frame()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    [Fact(DisplayName = "RFC-9114-4.1-enc-002: POST with body produces HEADERS + DATA frames")]
    public void Post_with_body_produces_headers_and_data()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain"),
        };

        var frames = encoder.Encode(request);

        Assert.Equal(2, frames.Count);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
        Assert.IsType<Http3DataFrame>(frames[1]);
    }

    [Fact(DisplayName = "RFC-9114-4.1-enc-003: POST with empty body produces HEADERS only")]
    public void Post_with_empty_body_produces_headers_only()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    [Fact(DisplayName = "RFC-9114-4.1-enc-004: DATA frame contains exact request body")]
    public void Data_frame_contains_exact_body()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var body = "Hello, HTTP/3!"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Put, "https://example.com/resource")
        {
            Content = new ByteArrayContent(body),
        };

        var frames = encoder.Encode(request);

        var dataFrame = Assert.IsType<Http3DataFrame>(frames[1]);
        Assert.Equal(body, dataFrame.Data.ToArray());
    }

    [Fact(DisplayName = "RFC-9114-4.1-enc-005: DELETE request without body produces single HEADERS frame")]
    public void Delete_without_body_produces_single_headers()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Delete, "https://example.com/item/42");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    // ───────────────────────── Pseudo-Headers ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-006: All four pseudo-headers present in encoded output")]
    public void All_four_pseudo_headers_present()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":method" && h.Value == "GET");
        Assert.Contains(headers, h => h.Name == ":path" && h.Value == "/path");
        Assert.Contains(headers, h => h.Name == ":scheme" && h.Value == "https");
        Assert.Contains(headers, h => h.Name == ":authority" && h.Value == "example.com");
    }

    [Theory(DisplayName = "RFC-9114-4.3.1-enc-007: :method reflects HTTP method")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void Method_pseudo_header_reflects_http_method(string method)
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":method" && h.Value == method);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-008: :path includes query string")]
    public void Path_includes_query_string()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=test&page=2");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":path" && h.Value == "/search?q=test&page=2");
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-009: :path without query string")]
    public void Path_without_query_string()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":path" && h.Value == "/resource");
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-010: :scheme is https for HTTPS URI")]
    public void Scheme_is_https()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":scheme" && h.Value == "https");
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-011: :scheme is http for HTTP URI")]
    public void Scheme_is_http()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":scheme" && h.Value == "http");
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-012: :authority includes non-default port")]
    public void Authority_includes_non_default_port()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8443/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":authority" && h.Value == "example.com:8443");
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-013: :authority omits default HTTPS port")]
    public void Authority_omits_default_https_port()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:443/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":authority" && h.Value == "example.com");
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-014: Pseudo-headers appear before regular headers")]
    public void Pseudo_headers_appear_first()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("accept", "text/html");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        var lastPseudoIdx = -1;
        var firstRegularIdx = int.MaxValue;
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Name.StartsWith(':'))
            {
                lastPseudoIdx = i;
            }
            else if (firstRegularIdx == int.MaxValue)
            {
                firstRegularIdx = i;
            }
        }

        Assert.True(lastPseudoIdx < firstRegularIdx, "Pseudo-headers must precede regular headers");
    }

    // ───────────────────────── QPACK Encoding ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-enc-015: HEADERS frame contains non-empty QPACK header block")]
    public void Headers_frame_contains_qpack_block()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);

        Assert.True(headersFrame.HeaderBlock.Length > 0, "QPACK header block must not be empty");
    }

    [Fact(DisplayName = "RFC-9114-4.1-enc-016: QPACK header block is decodable")]
    public void Qpack_header_block_decodable()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);

        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);
        Assert.True(headers.Count >= 4, "Should have at least 4 pseudo-headers");
    }

    [Fact(DisplayName = "RFC-9114-4.1-enc-017: Dynamic table enabled emits encoder instructions")]
    public void Dynamic_table_emits_encoder_instructions()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 4096);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("x-custom-header", "custom-value");

        encoder.Encode(request);

        Assert.True(encoder.EncoderInstructions.Length > 0,
            "Encoder should emit instructions when dynamic table is enabled");
    }

    [Fact(DisplayName = "RFC-9114-4.1-enc-018: Static table only (capacity=0) emits no encoder instructions")]
    public void Static_table_only_emits_no_instructions()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        encoder.Encode(request);

        Assert.Equal(0, encoder.EncoderInstructions.Length);
    }

    [Fact(DisplayName = "RFC-9114-4.1-enc-019: EncodeToQpackBlock returns raw QPACK block")]
    public void EncodeToQpackBlock_returns_raw_block()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");

        var block = encoder.EncodeToQpackBlock(request);
        var headers = decoder.Decode(block.Span);

        Assert.Contains(headers, h => h.Name == ":method" && h.Value == "GET");
        Assert.Contains(headers, h => h.Name == ":path" && h.Value == "/test");
    }

    // ───────────────────────── Regular Headers ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-enc-020: Custom request headers included in encoding")]
    public void Custom_headers_included()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-request-id", "abc-123");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == "accept" && h.Value == "application/json");
        Assert.Contains(headers, h => h.Name == "x-request-id" && h.Value == "abc-123");
    }

    [Fact(DisplayName = "RFC-9114-4.1-enc-021: Content headers included for request with body")]
    public void Content_headers_included()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == "content-type" && h.Value.Contains("application/json"));
    }

    [Fact(DisplayName = "RFC-9114-4.1-enc-022: Header names lowercased in encoding")]
    public void Header_names_lowercased()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == "accept-language" && h.Value == "en-US");
        Assert.DoesNotContain(headers, h => h.Name == "Accept-Language");
    }

    // ───────────────────────── Forbidden Headers ─────────────────────────

    [Theory(DisplayName = "RFC-9114-4.2-enc-023: Connection-specific headers filtered out")]
    [InlineData("connection")]
    [InlineData("transfer-encoding")]
    [InlineData("upgrade")]
    [InlineData("proxy-connection")]
    [InlineData("keep-alive")]
    public void Forbidden_headers_filtered(string forbiddenHeader)
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation(forbiddenHeader, "some-value");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.DoesNotContain(headers, h => h.Name == forbiddenHeader);
    }

    [Fact(DisplayName = "RFC-9114-4.2-enc-024: Non-forbidden headers preserved alongside forbidden")]
    public void Non_forbidden_headers_preserved()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("connection", "keep-alive");
        request.Headers.TryAddWithoutValidation("accept", "*/*");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.DoesNotContain(headers, h => h.Name == "connection");
        Assert.Contains(headers, h => h.Name == "accept" && h.Value == "*/*");
    }

    // ───────────────────────── Validation / Error Handling ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-025: Null request throws ArgumentNullException")]
    public void Null_request_throws()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        Assert.Throws<ArgumentNullException>(() => encoder.Encode(null!));
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-026: Null request URI throws ArgumentNullException")]
    public void Null_uri_throws()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);
        Assert.Throws<ArgumentNullException>(() => encoder.Encode(request));
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-027: ValidatePseudoHeaders rejects duplicate :method")]
    public void Validate_rejects_duplicate_method()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":method", "POST"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-028: ValidatePseudoHeaders rejects missing pseudo-headers")]
    public void Validate_rejects_missing_pseudo_headers()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            // missing :path, :scheme, :authority
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Missing", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-029: ValidatePseudoHeaders rejects unknown pseudo-header")]
    public void Validate_rejects_unknown_pseudo_header()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":unknown", "value"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("Unknown", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-030: ValidatePseudoHeaders rejects pseudo after regular")]
    public void Validate_rejects_pseudo_after_regular()
    {
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
            ("accept", "text/html"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<Http3ConnectionException>(
            () => Http3RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http3ErrorCode.MessageError, ex.ErrorCode);
        Assert.Contains("after regular", ex.Message);
    }

    // ───────────────────────── Stateful Encoder ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-enc-031: Encoder is stateful — second request benefits from QPACK state")]
    public void Encoder_is_stateful_across_requests()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 4096);

        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page1");
        var frames1 = encoder.Encode(request1);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/page2");
        var frames2 = encoder.Encode(request2);

        // Both encode successfully
        Assert.NotEmpty(frames1);
        Assert.NotEmpty(frames2);

        // Second request header block is typically smaller due to QPACK dynamic table reuse
        var block1 = Assert.IsType<Http3HeadersFrame>(frames1[0]).HeaderBlock;
        var block2 = Assert.IsType<Http3HeadersFrame>(frames2[0]).HeaderBlock;
        Assert.True(block2.Length <= block1.Length,
            "Second request should benefit from QPACK state (same or smaller header block)");
    }

    [Fact(DisplayName = "RFC-9114-4.1-enc-032: QpackEncoder property exposes underlying encoder")]
    public void QpackEncoder_property_accessible()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 4096);
        Assert.NotNull(encoder.QpackEncoder);
    }

    // ───────────────────────── Large Body ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.1-enc-033: Large request body encoded in DATA frame")]
    public void Large_body_encoded()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var body = new byte[64 * 1024]; // 64 KB
        new Random(42).NextBytes(body);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = new ByteArrayContent(body),
        };

        var frames = encoder.Encode(request);

        Assert.Equal(2, frames.Count);
        var dataFrame = Assert.IsType<Http3DataFrame>(frames[1]);
        Assert.Equal(body.Length, dataFrame.Data.Length);
        Assert.Equal(body, dataFrame.Data.ToArray());
    }

    // ───────────────────────── Root Path ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.3.1-enc-034: Root path encoded as /")]
    public void Root_path_encoded()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h.Name == ":path" && h.Value == "/");
    }
}
