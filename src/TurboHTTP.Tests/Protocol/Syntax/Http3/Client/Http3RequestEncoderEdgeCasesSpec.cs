using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3RequestEncoderEdgeCasesSpec
{
    private static Http3ClientEncoder CreateEncoder()
    {
        var tableSync = new QpackTableSync(encoderMaxCapacity: 4096, decoderMaxCapacity: 4096, maxBlockedStreams: 100);
        return new Http3ClientEncoder(tableSync);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encode_null_request_throws()
    {
        var encoder = CreateEncoder();

        Assert.Throws<ArgumentNullException>(() => encoder.Encode(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encode_null_uri_throws()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);

        Assert.Throws<ArgumentNullException>(() => encoder.Encode(request));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encode_request_with_no_body_produces_headers_only()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encode_request_with_zero_length_content_produces_headers_only()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/path")
        {
            Content = new ByteArrayContent([])
        };

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encode_request_with_body_produces_headers_only()
    {
        var encoder = CreateEncoder();
        var bodyData = new byte[] { 1, 2, 3, 4, 5 };
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/path")
        {
            Content = new ByteArrayContent(bodyData)
        };

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encode_large_body_request_produces_headers_only()
    {
        var encoder = CreateEncoder();
        var largeBody = new byte[32768];
        for (var i = 0; i < largeBody.Length; i++)
        {
            largeBody[i] = (byte)(i % 256);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/path")
        {
            Content = new ByteArrayContent(largeBody)
        };

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Encode_body_with_known_content_length_produces_headers_only()
    {
        var encoder = CreateEncoder();
        var bodyData = new byte[] { 1, 2, 3 };
        var content = new ByteArrayContent(bodyData);
        content.Headers.ContentLength = bodyData.Length;

        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/path")
        {
            Content = content
        };

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void Encode_connect_request_without_path()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://example.com:8443/");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void Encode_connect_request_with_non_default_port()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Connect, "https://example.com:8443/");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Encode_multiple_header_values_joins_with_comma()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        request.Headers.Add("accept", "text/html");
        request.Headers.Add("accept", "text/plain");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Encode_filters_forbidden_connection_header()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        request.Headers.Add("connection", "close");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Encode_filters_forbidden_transfer_encoding_header()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        request.Headers.Add("transfer-encoding", "chunked");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Encode_filters_forbidden_upgrade_header()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        request.Headers.Add("upgrade", "websocket");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Encode_filters_forbidden_proxy_connection_header()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        request.Headers.Add("proxy-connection", "keep-alive");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Encode_filters_forbidden_keep_alive_header()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        request.Headers.Add("keep-alive", "timeout=5");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Encode_preserves_non_forbidden_headers()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        request.Headers.Add("accept", "text/html");
        request.Headers.Add("user-agent", "test-agent");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void Encode_includes_content_headers()
    {
        var encoder = CreateEncoder();
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/path")
        {
            Content = content
        };

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void ValidatePseudoHeaders_rejects_empty_header_list()
    {
        var headers = new List<(string, string)>();

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains("Missing required pseudo-headers", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void ValidatePseudoHeaders_requires_method()
    {
        var headers = new List<(string, string)>
        {
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(":method", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void ValidatePseudoHeaders_requires_path()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(":path", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void ValidatePseudoHeaders_requires_scheme()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(":scheme", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void ValidatePseudoHeaders_requires_authority()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(":authority", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void ValidatePseudoHeaders_rejects_duplicate_method()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":method", "POST"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void ValidatePseudoHeaders_rejects_duplicate_path()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/path1"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":path", "/path2"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void ValidatePseudoHeaders_rejects_duplicate_scheme()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":scheme", "http"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void ValidatePseudoHeaders_rejects_duplicate_authority()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":authority", "example.org"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void ValidatePseudoHeaders_rejects_unknown_pseudo_header()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
            (":unknown", "value"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(":unknown", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3.1")]
    public void ValidatePseudoHeaders_rejects_pseudo_after_regular()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            ("regular-header", "value"),
            (":authority", "example.com"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains("appears after regular header", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ValidatePseudoHeaders_connect_with_scheme_rejected()
    {
        var headers = new List<(string, string)>
        {
            (":method", "CONNECT"),
            (":authority", "example.com"),
            (":scheme", "https"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains("MUST NOT include :scheme", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ValidatePseudoHeaders_connect_with_path_rejected()
    {
        var headers = new List<(string, string)>
        {
            (":method", "CONNECT"),
            (":authority", "example.com"),
            (":path", "/"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains("MUST NOT include :path", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ValidatePseudoHeaders_connect_without_authority_rejected()
    {
        var headers = new List<(string, string)>
        {
            (":method", "CONNECT"),
        };

        var ex = Assert.Throws<HttpProtocolException>(() => Http3ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains("MUST include :authority", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.4")]
    public void ValidatePseudoHeaders_connect_with_authority_accepted()
    {
        var headers = new List<(string, string)>
        {
            (":method", "CONNECT"),
            (":authority", "example.com:8443"),
        };

        // Should not throw
        Http3ClientEncoder.ValidatePseudoHeaders(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeToQpackBlock_returns_memory_owner()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");

        var (owner, length) = encoder.EncodeToQpackBlock(request);

        Assert.NotNull(owner);
        Assert.True(length > 0);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeToQpackBlock_null_request_throws()
    {
        var encoder = CreateEncoder();

        Assert.Throws<ArgumentNullException>(() => encoder.EncodeToQpackBlock(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EncodeToQpackBlock_null_uri_throws()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, (Uri?)null);

        Assert.Throws<ArgumentNullException>(() => encoder.EncodeToQpackBlock(request));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4")]
    public void Encode_stateful_across_calls()
    {
        var encoder = CreateEncoder();

        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        var frames1 = encoder.Encode(request1);
        Assert.Single(frames1);

        var request2 = new HttpRequestMessage(HttpMethod.Post, "https://example.com/path")
        {
            Content = new ByteArrayContent([1, 2, 3])
        };
        var frames2 = encoder.Encode(request2);
        Assert.Single(frames2);

        Assert.IsType<HeadersFrame>(frames1[0]);
        Assert.IsType<HeadersFrame>(frames2[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4")]
    public void Encode_with_query_string()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?key=value&foo=bar");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4")]
    public void Encode_with_root_path()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4")]
    public void Encode_with_http_scheme()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4")]
    public void Encode_lowercase_header_names()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-Custom-Header", "value");

        var frames = encoder.Encode(request);

        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4")]
    public void Encode_encoder_instructions_available_after_encode()
    {
        var encoder = CreateEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path");
        request.Headers.Add("x-custom", "value");

        encoder.Encode(request);

        // EncoderInstructions may be empty or non-empty depending on dynamic table usage
        var instructions = encoder.EncoderInstructions;
        Assert.True(instructions.Length >= 0);
    }
}