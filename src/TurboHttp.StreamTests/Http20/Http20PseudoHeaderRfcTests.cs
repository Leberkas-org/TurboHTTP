using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http20;

public sealed class Http20PseudoHeaderRfcTests : StreamTestBase
{
    private static HttpRequestMessage GetRequest(string uri = "http://example.com/")
        => new(HttpMethod.Get, uri);

    private static HttpRequestMessage PostRequest(string uri = "http://example.com/", string body = "hello")
        => new(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
        };

    /// <summary>
    /// Runs requests through StreamIdAllocatorStage → Request2FrameStage and collects all frames.
    /// </summary>
    private async Task<IReadOnlyList<Http2Frame>> RunAsync(params HttpRequestMessage[] requests)
    {
        var encoder = new Http2RequestEncoder();

        return await Source.From(requests)
            .Via(Flow.FromGraph(new StreamIdAllocatorStage()))
            .Via(Flow.FromGraph(new Request2FrameStage(encoder)))
            .RunWith(Sink.Seq<Http2Frame>(), Materializer);
    }

    /// <summary>
    /// Decodes the HPACK header block from a HEADERS frame into a list of header fields.
    /// </summary>
    private static List<HpackHeader> DecodeHeaders(HeadersFrame frame)
        => new HpackDecoder().Decode(frame.HeaderBlockFragment.Span);

    // ─── H2PH-001: :method = HTTP method (GET, POST, etc.) ─────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-001: :method pseudo-header equals GET for GET request")]
    public async Task H2PH_001_Method_Get()
    {
        var frames = await RunAsync(GetRequest());

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var method = Assert.Single(headers, h => h.Name == ":method");
        Assert.Equal("GET", method.Value);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-001: :method pseudo-header equals POST for POST request")]
    public async Task H2PH_001_Method_Post()
    {
        var frames = await RunAsync(PostRequest());

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var method = Assert.Single(headers, h => h.Name == ":method");
        Assert.Equal("POST", method.Value);
    }

    [Theory(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-001: :method pseudo-header matches HTTP method")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("OPTIONS")]
    [InlineData("HEAD")]
    public async Task H2PH_001_Method_Various(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "http://example.com/");
        if (method is "PUT" or "PATCH")
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("body"));
        }

        var frames = await RunAsync(request);

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var methodHeader = Assert.Single(headers, h => h.Name == ":method");
        Assert.Equal(method, methodHeader.Value);
    }

    // ─── H2PH-002: :path = absolute path + query ───────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-002: :path pseudo-header equals absolute path")]
    public async Task H2PH_002_Path_Simple()
    {
        var frames = await RunAsync(GetRequest("http://example.com/api/items"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var path = Assert.Single(headers, h => h.Name == ":path");
        Assert.Equal("/api/items", path.Value);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-002: :path pseudo-header includes query string")]
    public async Task H2PH_002_Path_With_Query()
    {
        var frames = await RunAsync(GetRequest("http://example.com/search?q=foo&page=2"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var path = Assert.Single(headers, h => h.Name == ":path");
        Assert.Equal("/search?q=foo&page=2", path.Value);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-002: :path pseudo-header for root is /")]
    public async Task H2PH_002_Path_Root()
    {
        var frames = await RunAsync(GetRequest("http://example.com/"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var path = Assert.Single(headers, h => h.Name == ":path");
        Assert.Equal("/", path.Value);
    }

    // ─── H2PH-003: :scheme = URI scheme (http/https) ───────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-003: :scheme pseudo-header equals http for http URI")]
    public async Task H2PH_003_Scheme_Http()
    {
        var frames = await RunAsync(GetRequest("http://example.com/"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var scheme = Assert.Single(headers, h => h.Name == ":scheme");
        Assert.Equal("http", scheme.Value);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-003: :scheme pseudo-header equals https for https URI")]
    public async Task H2PH_003_Scheme_Https()
    {
        var frames = await RunAsync(GetRequest("https://example.com/"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var scheme = Assert.Single(headers, h => h.Name == ":scheme");
        Assert.Equal("https", scheme.Value);
    }

    // ─── H2PH-004: :authority = host:port ───────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-004: :authority pseudo-header equals host")]
    public async Task H2PH_004_Authority_Host()
    {
        var frames = await RunAsync(GetRequest("http://example.com/"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var authority = Assert.Single(headers, h => h.Name == ":authority");
        Assert.Equal("example.com", authority.Value);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-004: :authority pseudo-header includes port when non-default")]
    public async Task H2PH_004_Authority_Host_With_Port()
    {
        var frames = await RunAsync(GetRequest("http://example.com:8080/api"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));
        var authority = Assert.Single(headers, h => h.Name == ":authority");
        Assert.Equal("example.com:8080", authority.Value);
    }

    // ─── H2PH-005: Pseudo-headers appear BEFORE regular headers ─────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-005: All pseudo-headers appear before regular headers")]
    public async Task H2PH_005_Pseudo_Headers_Before_Regular()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        request.Headers.Add("x-custom", "value");
        request.Headers.Add("accept", "text/html");

        var frames = await RunAsync(request);

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));

        // Find the index of the first non-pseudo header
        var firstRegularIndex = headers.FindIndex(h => !h.Name.StartsWith(':'));
        Assert.True(firstRegularIndex > 0, "There must be at least one pseudo-header before regular headers");

        // All headers before firstRegularIndex must be pseudo-headers
        for (var i = 0; i < firstRegularIndex; i++)
        {
            Assert.True(headers[i].Name.StartsWith(':'),
                $"Header at index {i} ('{headers[i].Name}') should be a pseudo-header");
        }

        // No pseudo-headers after the first regular header
        for (var i = firstRegularIndex; i < headers.Count; i++)
        {
            Assert.False(headers[i].Name.StartsWith(':'),
                $"Pseudo-header '{headers[i].Name}' found at index {i} after regular headers");
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-9113-§8.3.1-H2PH-005: All four required pseudo-headers are present")]
    public async Task H2PH_005_All_Four_Pseudo_Headers_Present()
    {
        var frames = await RunAsync(GetRequest("http://example.com/path"));

        var headers = DecodeHeaders(Assert.IsType<HeadersFrame>(frames[0]));

        var pseudoHeaders = headers.Where(h => h.Name.StartsWith(':')).ToList();
        var pseudoNames = pseudoHeaders.Select(h => h.Name).ToHashSet();

        Assert.Contains(":method", pseudoNames);
        Assert.Contains(":path", pseudoNames);
        Assert.Contains(":scheme", pseudoNames);
        Assert.Contains(":authority", pseudoNames);
    }
}
