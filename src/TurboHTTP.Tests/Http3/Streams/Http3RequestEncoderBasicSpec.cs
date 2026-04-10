using System.Text;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Streams;

/// <summary>
/// RFC 9114 §4.1, §4.3.1 — Http3RequestEncoder basic encoding tests.
/// Covers frame structure, pseudo-header construction, QPACK block encoding, and
/// dynamic table behaviour for GET/POST/PUT/DELETE requests.
/// </summary>
public sealed class Http3RequestEncoderBasicSpec
{

    [Fact]
    [Trait("RFC", "RFC9114-4.1")]
    public void Get_request_produces_single_headers_frame()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.1")]
    public void Post_with_empty_body_produces_headers_only()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api")
        {
            Content = new ByteArrayContent([]),
        };

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.1")]
    public void Delete_without_body_produces_single_headers()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Delete, "https://example.com/item/42");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
    }


    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void All_four_pseudo_headers_present()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: ":method", Value: "GET" });
        Assert.Contains(headers, h => h is { Name: ":path", Value: "/path" });
        Assert.Contains(headers, h => h is { Name: ":scheme", Value: "https" });
        Assert.Contains(headers, h => h is { Name: ":authority", Value: "example.com" });
    }

    [Theory]
    [Trait("RFC", "RFC9114-4.3.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Path_includes_query_string()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=test&page=2");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: ":path", Value: "/search?q=test&page=2" });
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Path_without_query_string()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: ":path", Value: "/resource" });
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Scheme_is_https()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: ":scheme", Value: "https" });
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Scheme_is_http()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: ":scheme", Value: "http" });
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Authority_includes_non_default_port()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8443/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: ":authority", Value: "example.com:8443" });
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void Authority_omits_default_https_port()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:443/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        var headers = decoder.Decode(headersFrame.HeaderBlock.Span);

        Assert.Contains(headers, h => h is { Name: ":authority", Value: "example.com" });
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.3.1")]
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


    [Fact]
    [Trait("RFC", "RFC9114-4.1")]
    public void Headers_frame_contains_qpack_block()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var frames = encoder.Encode(request);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);

        Assert.True(headersFrame.HeaderBlock.Length > 0, "QPACK header block must not be empty");
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-4.1")]
    public void Dynamic_table_emits_encoder_instructions()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 4096);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("x-custom-header", "custom-value");

        encoder.Encode(request);

        Assert.True(encoder.EncoderInstructions.Length > 0,
            "Encoder should emit instructions when dynamic table is enabled");
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.1")]
    public void Static_table_only_emits_no_instructions()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        encoder.Encode(request);

        Assert.Equal(0, encoder.EncoderInstructions.Length);
    }

    [Fact]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeToQpackBlock_returns_raw_block()
    {
        var encoder = new Http3RequestEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");

        var (owner, length) = encoder.EncodeToQpackBlock(request);
        using var _ = owner;
        var headers = decoder.Decode(owner.Memory[..length].Span);

        Assert.Contains(headers, h => h is { Name: ":method", Value: "GET" });
        Assert.Contains(headers, h => h is { Name: ":path", Value: "/test" });
    }
}
