using System.Text;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Phase 29-30: Http2RequestEncoder — Pseudo-Header Validation (RFC 7540 §8.1.2.1)
/// Part 1: Contract tests for ValidatePseudoHeaders.
/// Part 2: Integration tests through Encode().
/// </summary>
public sealed class Http2EncoderPseudoHeaderTests
{
    // =========================================================================
    // PART 1: Contract Tests for ValidatePseudoHeaders
    // =========================================================================

    // --- Happy Path ----------------------------------------------------------

    [Theory(DisplayName = "RFC9113-8.1.2.1-c001: Valid pseudo-header configurations pass validation")]
    [InlineData("/path", "GET", "https", "example.com", 0)]
    [InlineData("/", "GET", "https", "example.com", 1)]
    [InlineData("/api", "POST", "https", "api.example.com", 3)]
    [InlineData("/", "HEAD", "http", "host.example.com", 0)]
    public void Validate_ValidHeaders_Pass(string path, string method, string scheme, string authority, int regularHeaderCount)
    {
        var headers = AllFourPseudos(path, method, scheme, authority);
        for (var i = 0; i < regularHeaderCount; i++)
        {
            headers.Add(($"x-header-{i}", $"value-{i}"));
        }

        var ex = Record.Exception(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Null(ex);
    }

    // --- Missing Required Pseudo-Headers ------------------------------------

    [Theory(DisplayName = "RFC9113-8.1.2.1-c005: Missing [{missingHeader}] throws Http2Exception")]
    [InlineData(":method")]
    [InlineData(":path")]
    [InlineData(":scheme")]
    [InlineData(":authority")]
    public void Validate_MissingSinglePseudoHeader_Throws(string missingHeader)
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.RemoveAll(h => h.Item1 == missingHeader);
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(missingHeader, ex.Message);
    }

    [Theory(DisplayName = "RFC9113-8.1.2.1-c009: Multiple missing pseudo-headers listed in error message")]
    [InlineData(false, ":method,:path,:scheme,:authority")]
    [InlineData(true, ":path,:scheme,:authority")]
    public void Validate_MultipleMissing_AllListedInMessage(bool includeMethod, string expectedMissingCsv)
    {
        var headers = new List<(string, string)>();
        if (includeMethod)
        {
            headers.Add((":method", "GET"));
        }

        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        foreach (var expected in expectedMissingCsv.Split(','))
        {
            Assert.Contains(expected, ex.Message);
        }
    }

    // --- Duplicate Pseudo-Headers -------------------------------------------

    [Theory(DisplayName = "RFC9113-8.1.2.1-c011: Duplicate [{pseudoHeader}] throws Http2Exception")]
    [InlineData(":method", "POST")]
    [InlineData(":path", "/second")]
    [InlineData(":scheme", "http")]
    [InlineData(":authority", "other.com")]
    public void Validate_DuplicatePseudoHeader_Throws(string pseudoHeader, string duplicateValue)
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Insert(1, (pseudoHeader, duplicateValue));
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(pseudoHeader, ex.Message);
    }

    // --- Unknown Pseudo-Headers ---------------------------------------------

    [Theory(DisplayName = "RFC9113-8.1.2.1-c015: Unknown pseudo-header [{unknownHeader}] throws Http2Exception")]
    [InlineData(":status", "200")]
    [InlineData(":custom", "value")]
    public void Validate_UnknownPseudoHeader_Throws(string unknownHeader, string value)
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Add((unknownHeader, value));
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(unknownHeader, ex.Message);
    }

    // --- Pseudo-Headers After Regular Headers --------------------------------

    [Theory(DisplayName = "RFC9113-8.1.2.1-c017: Pseudo-header after regular header at index [{insertIndex}] throws")]
    [InlineData(2, "x-custom", "value")]
    [InlineData(1, "host", "example.com")]
    [InlineData(1, "x-header", "val")]
    public void Validate_PseudoAfterRegular_Throws(int insertIndex, string regularName, string regularValue)
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Insert(insertIndex, (regularName, regularValue));
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9113-8.1.2.1-c018: Pseudo-after-regular error message contains indices")]
    public void Validate_PseudoAfterRegular_MessageContainsPositions()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            ("accept", "text/html"),  // regular at index 1
            (":path", "/"),           // pseudo at index 2 — INVALID
            (":scheme", "https"),
            (":authority", "example.com"),
        };
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        // Message should mention both the pseudo index and the regular header index
        Assert.Contains("2", ex.Message);
        Assert.Contains("1", ex.Message);
    }

    // =========================================================================
    // PART 2: Integration Tests via Encode()
    // =========================================================================

    // --- Standard Methods ---------------------------------------------------

    [Theory(DisplayName = "RFC9113-8.1.2.1-i001: Encode succeeds for [{method}] requests")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void Encode_StandardMethods_Succeed(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "https://example.com/api");
        var ex = Record.Exception(() => EncodeRequest(request));
        Assert.Null(ex);
    }

    // --- Pseudo-Header Value Round-Trip -------------------------------------

    [Theory(DisplayName = "RFC9113-8.1.2.1-i002: Encode [{method}] {url} produces [{expectedHeader}]=[{expectedValue}]")]
    [InlineData("GET", "https://example.com/", ":scheme", "https")]
    [InlineData("GET", "http://example.com/", ":scheme", "http")]
    [InlineData("GET", "https://example.com/search?q=hello&page=1", ":path", "/search?q=hello&page=1")]
    [InlineData("GET", "https://example.com/", ":path", "/")]
    [InlineData("GET", "https://example.com:443/", ":authority", "example.com")]
    [InlineData("GET", "https://example.com:8443/", ":authority", "example.com:8443")]
    [InlineData("DELETE", "https://api.example.com/resource/1", ":method", "DELETE")]
    [InlineData("GET", "https://example.com/api/users?role=admin&active=true", ":path", "/api/users?role=admin&active=true")]
    [InlineData("GET", "https://api.backend.internal/health", ":authority", "api.backend.internal")]
    [InlineData("POST", "https://example.com/submit", ":method", "POST")]
    [InlineData("PUT", "https://example.com/item/42", ":method", "PUT")]
    [InlineData("GET", "http://insecure.example.com/data", ":scheme", "http")]
    [InlineData("GET", "https://example.com/a/b/c/d/resource", ":path", "/a/b/c/d/resource")]
    public void Encode_PseudoHeaderValue_MatchesExpected(string method, string url, string expectedHeader, string expectedValue)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        var (_, data) = EncodeRequest(request);
        var headers = DecodeHeaderList(data);
        Assert.Equal(expectedValue, headers.First(h => h.Name == expectedHeader).Value);
    }

    // --- Long Path ----------------------------------------------------------

    [Fact(DisplayName = "RFC9113-8.1.2.1-i006: Long path encodes correctly in :path")]
    public void Encode_LongPath_EncodesCorrectly()
    {
        var longPath = "/" + string.Join("/", Enumerable.Range(1, 20).Select(i => $"segment{i}"));
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://example.com{longPath}");
        var (_, data) = EncodeRequest(request);
        var path = DecodeHeaderList(data).First(h => h.Name == ":path").Value;
        Assert.Equal(longPath, path);
    }

    // --- Pseudo-Header Presence & Count -------------------------------------

    [Theory(DisplayName = "RFC9113-8.1.2.1-i009: All four pseudo-headers present in encoded [{method}] request")]
    [InlineData("GET", "https://example.com/data", false, false)]
    [InlineData("POST", "https://example.com/api/items", false, true)]
    [InlineData("GET", "https://example.com/", false, false)]
    [InlineData("GET", "https://example.com/huffman-test", true, false)]
    public void Encode_AllFourPseudoHeaders_PresentAndCounted(string method, string url, bool useHuffman, bool includeBody)
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

    // --- Pseudo-Header Order & Structure ------------------------------------

    [Fact(DisplayName = "RFC9113-8.1.2.1-i010: Pseudo-headers precede regular headers in output")]
    public void Encode_PseudoHeaders_PrecedeRegular()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Add("X-Custom", "value");
        var (_, data) = EncodeRequest(request);
        var headers = DecodeHeaderList(data);

        var lastPseudo = headers.FindLastIndex(h => h.Name.StartsWith(':'));
        var firstRegular = headers.FindIndex(h => !h.Name.StartsWith(':'));

        Assert.True(lastPseudo < firstRegular, $"lastPseudo={lastPseudo} must be < firstRegular={firstRegular}");
    }

    [Fact(DisplayName = "RFC9113-8.1.2.1-i011: No duplicate pseudo-headers in encoded output")]
    public void Encode_NoDuplicatePseudoHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");
        var (_, data) = EncodeRequest(request);
        var pseudos = DecodeHeaderList(data)
            .Where(h => h.Name.StartsWith(':'))
            .Select(h => h.Name)
            .ToList();

        Assert.Equal(pseudos.Count, pseudos.Distinct().Count());
    }

    [Fact(DisplayName = "RFC9113-8.1.2.1-i012: No unknown pseudo-headers in encoded output")]
    public void Encode_NoUnknownPseudoHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, data) = EncodeRequest(request);
        var pseudos = DecodeHeaderList(data).Where(h => h.Name.StartsWith(':'));
        var known = new[] { ":method", ":path", ":scheme", ":authority" };

        Assert.All(pseudos, h => Assert.Contains(h.Name, known));
    }

    // --- Custom Headers Do Not Break Pseudo-Header Rules --------------------

    [Fact(DisplayName = "RFC9113-8.1.2.1-i013: Custom headers do not displace pseudo-headers")]
    public void Encode_WithCustomHeaders_PseudoHeadersUnaffected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Add("X-Request-Id", "abc");
        request.Headers.Add("Accept-Language", "en-US");
        var (_, data) = EncodeRequest(request);
        var headers = DecodeHeaderList(data);

        Assert.Contains(headers, h => h.Name == ":method" && h.Value == "GET");
        Assert.Contains(headers, h => h.Name == ":path" && h.Value == "/");
        Assert.Contains(headers, h => h.Name == ":scheme" && h.Value == "https");
        Assert.Contains(headers, h => h.Name == ":authority" && h.Value == "example.com");
    }

    [Fact(DisplayName = "RFC9113-8.1.2.1-i014: Connection-specific headers stripped but pseudo-headers preserved")]
    public void Encode_ConnectionHeadersStripped_PseudoHeadersPreserved()
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

    // --- Multiple Requests --------------------------------------------------

    [Fact(DisplayName = "RFC9113-8.1.2.1-i015: Multiple requests each have valid pseudo-headers")]
    public void Encode_MultipleRequests_EachHasValidPseudoHeaders()
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

    // =========================================================================
    // Helpers
    // =========================================================================

    private static List<(string, string)> AllFourPseudos(string path, string method, string scheme, string authority)
    {
        return
        [
            (":method", method),
            (":path", path),
            (":scheme", scheme),
            (":authority", authority),
        ];
    }

    private static (int StreamId, byte[] Data) EncodeRequest(HttpRequestMessage request, bool useHuffman = false)
    {
        var encoder = new Http2RequestEncoder(useHuffman);
        var headerBlock = encoder.EncodeToHpackBlock(request);

        // Wrap in HTTP/2 HEADERS frame format for DecodeHeaderList()
        var frame = new byte[9 + headerBlock.Length];
        frame[0] = (byte)(headerBlock.Length >> 16);
        frame[1] = (byte)(headerBlock.Length >> 8);
        frame[2] = (byte)headerBlock.Length;
        frame[3] = (byte)FrameType.Headers;
        frame[4] = 0x05; // END_HEADERS
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), 1); // streamId=1
        System.Array.Copy(headerBlock, 0, frame, 9, headerBlock.Length);

        return (1, frame);
    }

    private static List<HpackHeader> DecodeHeaderList(byte[] data)
    {
        var payloadLen = (data[0] << 16) | (data[1] << 8) | data[2];
        var headerBlock = data[9..(9 + payloadLen)];
        return new HpackDecoder().Decode(headerBlock).ToList();
    }
}
