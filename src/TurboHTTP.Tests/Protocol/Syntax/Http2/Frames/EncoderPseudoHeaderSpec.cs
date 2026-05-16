using System.Text;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Client;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Frames;

public sealed class Http2EncoderPseudoHeaderSpec
{
    [Theory(Timeout = 5000)]
    [InlineData("/path", "GET", "https", "example.com", 0)]
    [InlineData("/", "GET", "https", "example.com", 1)]
    [InlineData("/api", "POST", "https", "api.example.com", 3)]
    [InlineData("/", "HEAD", "http", "host.example.com", 0)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_pass_validation_when_all_required_pseudo_headers_present(string path,
        string method, string scheme, string authority, int regularHeaderCount)
    {
        var headers = AllFourPseudos(path, method, scheme, authority);
        for (var i = 0; i < regularHeaderCount; i++)
        {
            headers.Add(new HpackHeader($"x-header-{i}", $"value-{i}"));
        }

        var ex = Record.Exception(() => Http2ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Null(ex);
    }

    [Theory(Timeout = 5000)]
    [InlineData(":method")]
    [InlineData(":path")]
    [InlineData(":scheme")]
    [InlineData(":authority")]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_throw_http2_exception_when_single_required_pseudo_header_missing(
        string missingHeader)
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.RemoveAll(h => h.Name == missingHeader);
        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(missingHeader, ex.Message);
    }

    [Theory(Timeout = 5000)]
    [InlineData(false, ":method,:path,:scheme,:authority")]
    [InlineData(true, ":path,:scheme,:authority")]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_list_all_missing_headers_when_multiple_pseudo_headers_missing(
        bool includeMethod, string expectedMissingCsv)
    {
        var headers = new List<HpackHeader>();
        if (includeMethod)
        {
            headers.Add(new HpackHeader(":method", "GET"));
        }

        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientEncoder.ValidatePseudoHeaders(headers));
        foreach (var expected in expectedMissingCsv.Split(','))
        {
            Assert.Contains(expected, ex.Message);
        }
    }

    [Theory(Timeout = 5000)]
    [InlineData(":method", "POST")]
    [InlineData(":path", "/second")]
    [InlineData(":scheme", "http")]
    [InlineData(":authority", "other.com")]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_throw_http2_exception_when_duplicate_pseudo_header_detected(
        string pseudoHeader, string duplicateValue)
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Insert(1, new HpackHeader(pseudoHeader, duplicateValue));
        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(pseudoHeader, ex.Message);
    }

    [Theory(Timeout = 5000)]
    [InlineData(":status", "200")]
    [InlineData(":custom", "value")]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_throw_http2_exception_when_unknown_pseudo_header_detected(
        string unknownHeader, string value)
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Add(new HpackHeader(unknownHeader, value));
        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(unknownHeader, ex.Message);
    }

    [Theory(Timeout = 5000)]
    [InlineData(2, "x-custom", "value")]
    [InlineData(1, "host", "example.com")]
    [InlineData(1, "x-header", "val")]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_throw_http2_exception_when_pseudo_header_appears_after_regular_header(
        int insertIndex, string regularName, string regularValue)
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Insert(insertIndex, new HpackHeader(regularName, regularValue));
        Assert.Throws<HttpProtocolException>(() => Http2ClientEncoder.ValidatePseudoHeaders(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_include_indices_in_message_when_pseudo_after_regular_header_violation()
    {
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new("accept", "text/html"), // regular at index 1
            new(":path", "/"), // pseudo at index 2 â€” INVALID
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };
        var ex = Assert.Throws<HttpProtocolException>(() => Http2ClientEncoder.ValidatePseudoHeaders(headers));
        // Message includes the last pseudo-header index (4) and the first regular header index (1)
        Assert.Contains("4", ex.Message);
        Assert.Contains("1", ex.Message);
    }

    [Theory(Timeout = 5000)]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_succeed_when_encoding_standard_http_methods(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/api");
        var ex = Record.Exception(() => EncodeRequest(request));
        Assert.Null(ex);
    }

    [Theory(Timeout = 5000)]
    [InlineData("GET", "https://example.com/", ":scheme", "https")]
    [InlineData("GET", "http://example.com/", ":scheme", "http")]
    [InlineData("GET", "https://example.com/search?q=hello&page=1", ":path", "/search?q=hello&page=1")]
    [InlineData("GET", "https://example.com/", ":path", "/")]
    [InlineData("GET", "https://example.com:443/", ":authority", "example.com")]
    [InlineData("GET", "https://example.com:8443/", ":authority", "example.com:8443")]
    [InlineData("DELETE", "https://api.example.com/resource/1", ":method", "DELETE")]
    [InlineData("GET", "https://example.com/api/users?role=admin&active=true", ":path",
        "/api/users?role=admin&active=true")]
    [InlineData("GET", "https://api.backend.internal/health", ":authority", "api.backend.internal")]
    [InlineData("POST", "https://example.com/submit", ":method", "POST")]
    [InlineData("PUT", "https://example.com/item/42", ":method", "PUT")]
    [InlineData("GET", "http://insecure.example.com/data", ":scheme", "http")]
    [InlineData("GET", "https://example.com/a/b/c/d/resource", ":path", "/a/b/c/d/resource")]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_match_expected_value_when_pseudo_header_encoded(string method, string url,
        string expectedHeader, string expectedValue)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        var (_, data) = EncodeRequest(request);
        var headers = DecodeHeaderList(data);
        Assert.Equal(expectedValue, headers.First(h => h.Name == expectedHeader).Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_encode_correctly_when_path_is_long()
    {
        var longPath = "/" + string.Join("/", Enumerable.Range(1, 20).Select(i => $"segment{i}"));
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://example.com{longPath}");
        var (_, data) = EncodeRequest(request);
        var path = DecodeHeaderList(data).First(h => h.Name == ":path").Value;
        Assert.Equal(longPath, path);
    }

    [Theory(Timeout = 5000)]
    [InlineData("GET", "https://example.com/data", false, false)]
    [InlineData("POST", "https://example.com/api/items", false, true)]
    [InlineData("GET", "https://example.com/", false, false)]
    [InlineData("GET", "https://example.com/huffman-test", true, false)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_have_all_four_pseudo_headers_present_when_encoding(string method, string url,
        bool useHuffman, bool includeBody)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (includeBody)
        {
            request.Content = new StringContent("{\"name\":\"test\"}", Encoding.UTF8, "application/json");
        }

        var (_, data) = EncodeRequest(request, useHuffman);
        var headers = DecodeHeaderList(data);
        var names = headers.Select(h => h.Name).ToList();
        var pseudoCount = headers.Count(h => h.Name.StartsWith(':'));

        Assert.Contains(":method", names);
        Assert.Contains(":path", names);
        Assert.Contains(":scheme", names);
        Assert.Contains(":authority", names);
        Assert.Equal(4, pseudoCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_precede_regular_headers_when_pseudo_headers_encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Add("X-Custom", "value");
        var (_, data) = EncodeRequest(request);
        var headers = DecodeHeaderList(data);

        var lastPseudo = headers.FindLastIndex(h => h.Name.StartsWith(':'));
        var firstRegular = headers.FindIndex(h => !h.Name.StartsWith(':'));

        Assert.True(lastPseudo < firstRegular, $"lastPseudo={lastPseudo} must be < firstRegular={firstRegular}");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_not_produce_duplicate_pseudo_headers_when_encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");
        var (_, data) = EncodeRequest(request);
        var pseudos = DecodeHeaderList(data)
            .Where(h => h.Name.StartsWith(':'))
            .Select(h => h.Name)
            .ToList();

        Assert.Equal(pseudos.Count, pseudos.Distinct().Count());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_not_produce_unknown_pseudo_headers_when_encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, data) = EncodeRequest(request);
        var pseudos = DecodeHeaderList(data).Where(h => h.Name.StartsWith(':'));
        var known = new[] { ":method", ":path", ":scheme", ":authority" };

        Assert.All(pseudos, h => Assert.Contains(h.Name, known));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_not_affect_pseudo_headers_when_custom_headers_added()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Add("X-Request-Id", "abc");
        request.Headers.Add("Accept-Language", "en-US");
        var (_, data) = EncodeRequest(request);
        var headers = DecodeHeaderList(data);

        Assert.Contains(headers, h => h is { Name: ":method", Value: "GET" });
        Assert.Contains(headers, h => h is { Name: ":path", Value: "/" });
        Assert.Contains(headers, h => h is { Name: ":scheme", Value: "https" });
        Assert.Contains(headers, h => h is { Name: ":authority", Value: "example.com" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_preserve_pseudo_headers_when_connection_headers_stripped()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Headers =
            {
                { "Connection", "keep-alive" },
                { "Transfer-Encoding", "chunked" },
                { "Upgrade", "websocket" },
            }
        };
        var (_, data) = EncodeRequest(request);
        var headers = DecodeHeaderList(data);
        var names = headers.Select(h => h.Name).ToList();

        Assert.Contains(":method", names);
        Assert.Contains(":path", names);
        Assert.Contains(":scheme", names);
        Assert.Contains(":authority", names);
        Assert.DoesNotContain("connection", names);
        Assert.DoesNotContain("transfer-encoding", names);
        Assert.DoesNotContain("upgrade", names);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.2.1")]
    public void Http2RequestEncoder_should_have_valid_pseudo_headers_when_multiple_requests_encoded()
    {
        // Use a fresh encoder per request to avoid HPACK dynamic table state
        // carrying over between decode calls with independent decoders.
        for (var i = 1; i <= 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://example.com/resource/{i}");
            var (_, data) = EncodeRequest(request, useHuffman: false);
            var headers = DecodeHeaderList(data);
            var names = headers.Select(h => h.Name).ToList();

            Assert.Contains(":method", names);
            Assert.Contains(":path", names);
            Assert.Contains(":scheme", names);
            Assert.Contains(":authority", names);
        }
    }

    // Helpers

    private static List<HpackHeader> AllFourPseudos(string path, string method, string scheme, string authority)
    {
        return
        [
            new HpackHeader(":method", method),
            new HpackHeader(":path", path),
            new HpackHeader(":scheme", scheme),
            new HpackHeader(":authority", authority),
        ];
    }

    private static (int StreamId, byte[] Data) EncodeRequest(HttpRequestMessage request, bool useHuffman = false)
    {
        var encoder = new Http2ClientEncoder(useHuffman);
        var headerBlock = encoder.EncodeToHpackBlock(request);

        // Wrap in HTTP/2 HEADERS frame format for DecodeHeaderList()
        var frame = new byte[9 + headerBlock.Length];
        frame[0] = (byte)(headerBlock.Length >> 16);
        frame[1] = (byte)(headerBlock.Length >> 8);
        frame[2] = (byte)headerBlock.Length;
        frame[3] = (byte)FrameType.Headers;
        frame[4] = 0x05; // END_HEADERS
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), 1); // streamId=1
        Array.Copy(headerBlock, 0, frame, 9, headerBlock.Length);

        return (1, frame);
    }

    private static List<HpackHeader> DecodeHeaderList(byte[] data)
    {
        var payloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var headerBlock = data[9..(9 + payloadLen)];
        return new HpackDecoder().Decode(headerBlock).ToList();
    }
}