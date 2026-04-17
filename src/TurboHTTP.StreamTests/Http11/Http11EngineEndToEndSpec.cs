using System.Net;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;
using TextEncoding = System.Text.Encoding;

namespace TurboHTTP.StreamTests.Http11;

/// <summary>
/// RFC-tagged round-trip tests for the HTTP/1.1 engine per RFC 9112.
/// Verifies end-to-end request encoding and response decoding through the full HTTP/1.1 protocol flow.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11Engine"/>.
/// RFC 9112 §3–§9: HTTP/1.1 full message exchange including chunked encoding and connection management.
/// </remarks>
public sealed class Http11EngineEndToEndSpec : EngineTestBase
{
    private static Http11Engine Engine => new(new Http1EngineOptions(16, 6, 3, 64 * 1024, 64, 1024 * 1024, TimeSpan.FromSeconds(2)));

    private static byte[] Ok200(string body) =>
        TextEncoding.Latin1.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n\r\n{body}");

    private static byte[] Ok200Empty() =>
        TextEncoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

    private static byte[] ChunkedResponse(string body)
    {
        var hex = body.Length.ToString("x");
        return TextEncoding.Latin1.GetBytes(
            $"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n{hex}\r\n{body}\r\n0\r\n\r\n");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11EngineEndToEnd_should_return_200_with_content_length_body_when_get_request()
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
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(responseBody, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11EngineEndToEnd_should_handle_chunked_request_and_response_when_post_with_chunked_encoding()
    {
        const string payload = "field=value&mode=chunked";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent(payload, TextEncoding.UTF8, "application/x-www-form-urlencoded")
        };
        request.Headers.TransferEncodingChunked = true;

        const string responseBody = "accepted";

        var (response, rawRequest) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => ChunkedResponse(responseBody));

        // Request wire must use chunked transfer encoding (no Content-Length for body)
        Assert.Contains("Transfer-Encoding: chunked", rawRequest);
        Assert.DoesNotContain("Content-Length: " + TextEncoding.UTF8.GetByteCount(payload), rawRequest);

        // Response must be decoded correctly from chunked transfer encoding
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(responseBody, body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11EngineEndToEnd_should_correlate_in_fifo_order_when_five_sequential_requests()
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

    [Theory(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-3.2")]
    [InlineData("http://api.example.com/v1", "Host: api.example.com\r\n")]
    [InlineData("http://other.example.com:9090/endpoint", "Host: other.example.com:9090\r\n")]
    [InlineData("https://secure.example.com/data", "Host: secure.example.com\r\n")]
    public async Task Http11EngineEndToEnd_should_set_correct_host_header_when_request_sent(string uri,
        string expectedHost)
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

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-5")]
    public async Task Http11EngineEndToEnd_should_strip_hop_by_hop_headers_when_sent_over_wire()
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

        // TE with non-chunked values is preserved per RFC 9112 §7.4 (listed in Connection)
        Assert.Contains("TE: trailers", rawRequest);
        Assert.DoesNotContain("Keep-Alive:", rawRequest);
        Assert.DoesNotContain("Proxy-Connection:", rawRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}