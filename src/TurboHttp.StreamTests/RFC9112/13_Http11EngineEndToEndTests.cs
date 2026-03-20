using System.Net;
using System.Text;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.RFC9112;

/// <summary>
/// RFC-tagged round-trip tests for the HTTP/1.1 engine per RFC 9112.
/// Verifies end-to-end request encoding and response decoding through the full HTTP/1.1 protocol flow.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11Engine"/>.
/// RFC 9112 §3–§9: HTTP/1.1 full message exchange including chunked encoding and connection management.
/// </remarks>
public sealed class Http11EngineEndToEndTests : EngineTestBase
{
    private static Http11Engine Engine => new();

    private static byte[] Ok200(string body) =>
        Encoding.Latin1.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n\r\n{body}");

    private static byte[] Ok200Empty() =>
        Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

    private static byte[] ChunkedResponse(string body)
    {
        var hex = body.Length.ToString("x");
        return Encoding.Latin1.GetBytes(
            $"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n{hex}\r\n{body}\r\n0\r\n\r\n");
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-6-11EN-001: GET → 200 with Content-Length body — version 1.1")]
    public async Task Should_Return200WithContentLengthBody_WhenGetRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/hello")
        {
            Version = HttpVersion.Version11
        };

        const string responseBody = "Hello, World!";

        var (response, _) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => Ok200(responseBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(responseBody, body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-7-11EN-002: POST → chunked request + chunked response")]
    public async Task Should_HandleChunkedRequestAndResponse_WhenPostWithChunkedEncoding()
    {
        const string payload = "field=value&mode=chunked";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        request.Headers.TransferEncodingChunked = true;

        const string responseBody = "accepted";

        var (response, rawRequest) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => ChunkedResponse(responseBody));

        // Request wire must use chunked transfer encoding (no Content-Length for body)
        Assert.Contains("Transfer-Encoding: chunked", rawRequest);
        Assert.DoesNotContain("Content-Length: " + Encoding.UTF8.GetByteCount(payload), rawRequest);

        // Response must be decoded correctly from chunked transfer encoding
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(responseBody, body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.3-11EN-003: 5 sequential requests → FIFO correlation")]
    public async Task Should_CorrelateInFifoOrder_WhenFiveSequentialRequests()
    {
        const int count = 5;
        var requests = Enumerable.Range(1, count)
            .Select(i =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/item/{i}")
                {
                    Version = HttpVersion.Version11
                };
                req.Headers.Add("X-Sequence", i.ToString());
                return req;
            })
            .ToList();

        var (responses, _) = await SendManyAsync(
            Engine.CreateFlow(),
            requests,
            Ok200Empty,
            count);

        Assert.Equal(count, responses.Count);

        for (var i = 0; i < count; i++)
        {
            Assert.NotNull(responses[i].RequestMessage);
            Assert.Same(requests[i], responses[i].RequestMessage);

            // Verify the correlated request is the correct one by sequence header
            var seq = responses[i].RequestMessage!.Headers.GetValues("X-Sequence").Single();
            Assert.Equal((i + 1).ToString(), seq);
        }
    }

    [Theory(Timeout = 10_000, DisplayName = "RFC9112-3.2-11EN-004: Host header in wire correct for each URI")]
    [InlineData("http://api.example.com/v1", "Host: api.example.com\r\n")]
    [InlineData("http://other.example.com:9090/endpoint", "Host: other.example.com:9090\r\n")]
    [InlineData("https://secure.example.com/data", "Host: secure.example.com\r\n")]
    public async Task Should_SetCorrectHostHeader_WhenRequestSent(string uri, string expectedHost)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = HttpVersion.Version11
        };

        var (_, rawRequest) = await SendAsync(
            Engine.CreateFlow(),
            request,
            Ok200Empty);

        Assert.Contains(expectedHost, rawRequest);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-5-11EN-005: Hop-by-hop headers (TE, Keep-Alive, Proxy-Connection) stripped in wire")]
    public async Task Should_StripHopByHopHeaders_WhenSentOverWire()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("Proxy-Connection", "keep-alive");

        var (response, rawRequest) = await SendAsync(
            Engine.CreateFlow(),
            request,
            Ok200Empty);

        Assert.DoesNotContain("TE:", rawRequest);
        Assert.DoesNotContain("Keep-Alive:", rawRequest);
        Assert.DoesNotContain("Proxy-Connection:", rawRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
