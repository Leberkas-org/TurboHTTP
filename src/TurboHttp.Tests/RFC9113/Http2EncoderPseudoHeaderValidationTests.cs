using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Phase 29-30: Http2RequestEncoder — Pseudo-Header Validation (RFC 7540 §8.1.2.1)
/// Part 1: 20 contract tests for ValidatePseudoHeaders directly.
/// Part 2: 25+ integration tests through Encode().
/// </summary>
public sealed class Http2RequestEncoderPseudoHeaderValidationTests
{
    // =========================================================================
    // PART 1: Contract Tests for ValidatePseudoHeaders (20 tests)
    // =========================================================================

    // --- Happy Path ----------------------------------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-c001: All four required pseudo-headers passes validation")]
    public void Validate_AllFourPseudoHeaders_Passes()
    {
        var headers = AllFourPseudos("/path", "GET", "https", "example.com");
        var ex = Record.Exception(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Null(ex);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c002: Pseudo-headers followed by regular headers passes")]
    public void Validate_PseudoThenRegular_Passes()
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Add(("content-type", "text/plain"));
        var ex = Record.Exception(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Null(ex);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c003: Multiple regular headers after pseudo-headers passes")]
    public void Validate_PseudoThenMultipleRegular_Passes()
    {
        var headers = AllFourPseudos("/api", "POST", "https", "api.example.com");
        headers.Add(("accept", "application/json"));
        headers.Add(("content-type", "application/json"));
        headers.Add(("x-custom-id", "abc123"));
        var ex = Record.Exception(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Null(ex);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c004: No regular headers (only pseudo-headers) passes")]
    public void Validate_OnlyPseudoHeaders_Passes()
    {
        var headers = AllFourPseudos("/", "HEAD", "http", "host.example.com");
        var ex = Record.Exception(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Null(ex);
    }

    // --- Missing Required Pseudo-Headers ------------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-c005: Missing :method throws Http2Exception")]
    public void Validate_MissingMethod_ThrowsHttp2Exception()
    {
        var headers = new List<(string, string)>
        {
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c006: Missing :path throws Http2Exception")]
    public void Validate_MissingPath_ThrowsHttp2Exception()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":path", ex.Message);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c007: Missing :scheme throws Http2Exception")]
    public void Validate_MissingScheme_ThrowsHttp2Exception()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":authority", "example.com"),
        };
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":scheme", ex.Message);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c008: Missing :authority throws Http2Exception")]
    public void Validate_MissingAuthority_ThrowsHttp2Exception()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
        };
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":authority", ex.Message);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c009: Empty header list throws with all missing pseudo-headers")]
    public void Validate_EmptyHeaders_ThrowsWithAllMissing()
    {
        var headers = new List<(string, string)>();
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
        Assert.Contains(":path", ex.Message);
        Assert.Contains(":scheme", ex.Message);
        Assert.Contains(":authority", ex.Message);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c010: Multiple missing pseudo-headers listed together in message")]
    public void Validate_MultipleMissing_AllListedInMessage()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
        };
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Contains(":path", ex.Message);
        Assert.Contains(":scheme", ex.Message);
        Assert.Contains(":authority", ex.Message);
    }

    // --- Duplicate Pseudo-Headers -------------------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-c011: Duplicate :method throws Http2Exception")]
    public void Validate_DuplicateMethod_Throws()
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Insert(1, (":method", "POST")); // duplicate :method at index 1
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":method", ex.Message);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c012: Duplicate :path throws Http2Exception")]
    public void Validate_DuplicatePath_Throws()
    {
        var headers = AllFourPseudos("/first", "GET", "https", "example.com");
        headers.Insert(1, (":path", "/second"));
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":path", ex.Message);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c013: Duplicate :scheme throws Http2Exception")]
    public void Validate_DuplicateScheme_Throws()
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Insert(1, (":scheme", "http"));
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":scheme", ex.Message);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c014: Duplicate :authority throws Http2Exception")]
    public void Validate_DuplicateAuthority_Throws()
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Insert(1, (":authority", "other.com"));
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":authority", ex.Message);
    }

    // --- Unknown Pseudo-Headers ---------------------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-c015: Unknown pseudo-header :status throws Http2Exception")]
    public void Validate_StatusPseudo_ThrowsForRequest()
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Insert(0, (":status", "200"));
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c016: Unknown pseudo-header :custom throws Http2Exception")]
    public void Validate_CustomPseudo_Throws()
    {
        var headers = AllFourPseudos("/", "GET", "https", "example.com");
        headers.Add((":custom", "value"));
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains(":custom", ex.Message);
    }

    // --- Pseudo-Headers After Regular Headers --------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-c017: Pseudo-header after regular header throws Http2Exception")]
    public void Validate_PseudoAfterRegular_Throws()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            ("x-custom", "value"),    // regular header at index 2
            (":scheme", "https"),     // pseudo after regular at index 3 — INVALID
            (":authority", "example.com"),
        };
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-8.1.2.1-c018: Pseudo-after-regular error message contains indices")]
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

    [Fact(DisplayName = "7540-8.1.2.1-c019: All pseudo-headers interleaved with regular headers throws")]
    public void Validate_InterleavedPseudoAndRegular_Throws()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            ("host", "example.com"),   // regular between pseudos — INVALID
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };
        Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
    }

    [Fact(DisplayName = "7540-8.1.2.1-c020: Error code on pseudo-after-regular is ProtocolError")]
    public void Validate_PseudoAfterRegular_ErrorCode_IsProtocolError()
    {
        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            ("x-header", "val"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };
        var ex = Assert.Throws<Http2Exception>(() => Http2RequestEncoder.ValidatePseudoHeaders(headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // =========================================================================
    // PART 2: Integration Tests via Encode() (25 tests)
    // =========================================================================

    // --- Standard Methods ---------------------------------------------------

    [Theory(DisplayName = "7540-8.1.2.1-i001: Encode succeeds for [{method}] requests")]
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

    // --- Scheme Encoding ----------------------------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-i002: Encode HTTPS request encodes :scheme as 'https'")]
    public void Encode_HttpsRequest_SchemeIsHttps()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, data) = EncodeRequest(request);
        var headers = DecodeHeaderList(data);
        Assert.Equal("https", headers.First(h => h.Name == ":scheme").Value);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i003: Encode HTTP request encodes :scheme as 'http'")]
    public void Encode_HttpRequest_SchemeIsHttp()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (_, data) = EncodeRequest(request);
        var headers = DecodeHeaderList(data);
        Assert.Equal("http", headers.First(h => h.Name == ":scheme").Value);
    }

    // --- Path Encoding ------------------------------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-i004: Encode encodes query string in :path")]
    public void Encode_WithQueryString_PathIncludesQuery()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/search?q=hello&page=1");
        var (_, data) = EncodeRequest(request);
        var path = DecodeHeaderList(data).First(h => h.Name == ":path").Value;
        Assert.Equal("/search?q=hello&page=1", path);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i005: Root path encodes :path as '/'")]
    public void Encode_RootPath_EncodesSlash()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, data) = EncodeRequest(request);
        var path = DecodeHeaderList(data).First(h => h.Name == ":path").Value;
        Assert.Equal("/", path);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i006: Long path encodes correctly in :path")]
    public void Encode_LongPath_EncodesCorrectly()
    {
        var longPath = "/" + string.Join("/", Enumerable.Range(1, 20).Select(i => $"segment{i}"));
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://example.com{longPath}");
        var (_, data) = EncodeRequest(request);
        var path = DecodeHeaderList(data).First(h => h.Name == ":path").Value;
        Assert.Equal(longPath, path);
    }

    // --- Authority Encoding -------------------------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-i007: Standard port not included in :authority")]
    public void Encode_StandardHttpsPort_AuthorityExcludesPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:443/");
        var (_, data) = EncodeRequest(request);
        var authority = DecodeHeaderList(data).First(h => h.Name == ":authority").Value;
        Assert.Equal("example.com", authority);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i008: Non-standard port included in :authority")]
    public void Encode_NonStandardPort_AuthorityIncludesPort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:8443/");
        var (_, data) = EncodeRequest(request);
        var authority = DecodeHeaderList(data).First(h => h.Name == ":authority").Value;
        Assert.Equal("example.com:8443", authority);
    }

    // --- Pseudo-Header Order & Presence -------------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-i009: All four pseudo-headers present in encoded output")]
    public void Encode_AllFourPseudoHeaders_Present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/data");
        var (_, data) = EncodeRequest(request);
        var names = DecodeHeaderList(data).Select(h => h.Name).ToList();

        Assert.Contains(":method", names);
        Assert.Contains(":path", names);
        Assert.Contains(":scheme", names);
        Assert.Contains(":authority", names);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i010: Pseudo-headers precede regular headers in output")]
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

    [Fact(DisplayName = "7540-8.1.2.1-i011: No duplicate pseudo-headers in encoded output")]
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

    [Fact(DisplayName = "7540-8.1.2.1-i012: No unknown pseudo-headers in encoded output")]
    public void Encode_NoUnknownPseudoHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, data) = EncodeRequest(request);
        var pseudos = DecodeHeaderList(data).Where(h => h.Name.StartsWith(':'));
        var known = new[] { ":method", ":path", ":scheme", ":authority" };

        Assert.All(pseudos, h => Assert.Contains(h.Name, known));
    }

    // --- Custom Headers Do Not Break Pseudo-Header Rules --------------------

    [Fact(DisplayName = "7540-8.1.2.1-i013: Custom headers do not displace pseudo-headers")]
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

    [Fact(DisplayName = "7540-8.1.2.1-i014: Connection-specific headers stripped but pseudo-headers preserved")]
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

    [Fact(DisplayName = "7540-8.1.2.1-i015: Multiple requests each have valid pseudo-headers")]
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

    // --- Correct Values Encoded ---------------------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-i016: :method value matches request method")]
    public void Encode_MethodValue_MatchesRequestMethod()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "https://api.example.com/resource/1");
        var (_, data) = EncodeRequest(request);
        var dict = DecodeHeaderList(data).ToDictionary(h => h.Name, h => h.Value);
        Assert.Equal("DELETE", dict[":method"]);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i017: :path value includes path and query string")]
    public void Encode_PathValue_IncludesPathAndQuery()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api/users?role=admin&active=true");
        var (_, data) = EncodeRequest(request);
        var dict = DecodeHeaderList(data).ToDictionary(h => h.Name, h => h.Value);
        Assert.Equal("/api/users?role=admin&active=true", dict[":path"]);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i018: :authority value matches URI host")]
    public void Encode_AuthorityValue_MatchesUriHost()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.backend.internal/health");
        var (_, data) = EncodeRequest(request);
        var dict = DecodeHeaderList(data).ToDictionary(h => h.Name, h => h.Value);
        Assert.Equal("api.backend.internal", dict[":authority"]);
    }

    // --- POST Requests with Body --------------------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-i019: POST request with body includes all pseudo-headers")]
    public void Encode_PostWithBody_AllPseudoHeadersPresent()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/items");
        request.Content = new StringContent("{\"name\":\"test\"}", Encoding.UTF8, "application/json");
        var (_, data) = EncodeRequest(request);
        var names = DecodeHeaderList(data).Select(h => h.Name).ToList();

        Assert.Contains(":method", names);
        Assert.Contains(":path", names);
        Assert.Contains(":scheme", names);
        Assert.Contains(":authority", names);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i020: POST :method value is POST")]
    public void Encode_Post_MethodIsPOST()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/submit");
        request.Content = new StringContent("data", Encoding.UTF8, "text/plain");
        var (_, data) = EncodeRequest(request);
        var dict = DecodeHeaderList(data).ToDictionary(h => h.Name, h => h.Value);
        Assert.Equal("POST", dict[":method"]);
    }

    // --- Encode then Decode Round-Trip -------------------------------------

    [Fact(DisplayName = "7540-8.1.2.1-i021: Encode-decode round trip preserves :method value")]
    public void Encode_Decode_RoundTrip_MethodPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "https://example.com/item/42");
        var (_, data) = EncodeRequest(request);
        var dict = DecodeHeaderList(data).ToDictionary(h => h.Name, h => h.Value);
        Assert.Equal("PUT", dict[":method"]);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i022: Encode-decode round trip preserves :scheme value")]
    public void Encode_Decode_RoundTrip_SchemePreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://insecure.example.com/data");
        var (_, data) = EncodeRequest(request);
        var dict = DecodeHeaderList(data).ToDictionary(h => h.Name, h => h.Value);
        Assert.Equal("http", dict[":scheme"]);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i023: Exactly four pseudo-headers in encoded GET request")]
    public void Encode_GetRequest_ExactlyFourPseudoHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, data) = EncodeRequest(request);
        var pseudoCount = DecodeHeaderList(data).Count(h => h.Name.StartsWith(':'));
        Assert.Equal(4, pseudoCount);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i024: :path for nested path encodes full path")]
    public void Encode_NestedPath_FullPathEncoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/a/b/c/d/resource");
        var (_, data) = EncodeRequest(request);
        var dict = DecodeHeaderList(data).ToDictionary(h => h.Name, h => h.Value);
        Assert.Equal("/a/b/c/d/resource", dict[":path"]);
    }

    [Fact(DisplayName = "7540-8.1.2.1-i025: Encode with Huffman compression still produces valid pseudo-headers")]
    public void Encode_WithHuffman_PseudoHeadersValid()
    {
        var (_, data) = EncodeRequest(new HttpRequestMessage(HttpMethod.Get, "https://example.com/huffman-test"), useHuffman: true);
        var headers = DecodeHeaderList(data);
        var names = headers.Select(h => h.Name).ToList();

        Assert.Contains(":method", names);
        Assert.Contains(":path", names);
        Assert.Contains(":scheme", names);
        Assert.Contains(":authority", names);
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
