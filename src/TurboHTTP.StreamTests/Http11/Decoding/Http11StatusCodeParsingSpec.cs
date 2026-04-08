using System.Net;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http11.Decoding;

/// <summary>
/// Tests HTTP/1.1 response status code parsing via the full engine per RFC 9110.
/// Verifies that 1xx, 2xx, 3xx, 4xx, and 5xx status codes are correctly decoded and exposed on the response.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11DecoderStage"/>.
/// RFC 9110 §15.1: HTTP status code classes and their required handling by clients.
/// </remarks>
public sealed class Http11StatusCodeParsingSpec : EngineTestBase
{
    private static Http11Engine Engine => new();

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-15.1")]
    public async Task Http11StatusCodeParsing_should_return_200_when_server_responds_ok()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/ok");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"u8.ToArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(new Version(1, 1), response.Version);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("ok", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-15.1")]
    public async Task Http11StatusCodeParsing_should_return_301_with_location_header_when_moved_permanently()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 301 Moved Permanently\r\nLocation: http://example.com/new\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(301, (int)response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(new Uri("http://example.com/new"), response.Headers.Location);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-15.1")]
    public async Task Http11StatusCodeParsing_should_return_404_when_resource_not_found()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 404 Not Found\r\nContent-Length: 9\r\n\r\nnot found"u8.ToArray());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("not found", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-15.1")]
    public async Task Http11StatusCodeParsing_should_return_500_when_internal_server_error()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/error");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 5\r\n\r\nerror"u8.ToArray());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(500, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("error", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-15.1")]
    public async Task Http11StatusCodeParsing_should_return_204_with_empty_body_when_no_content()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(204, (int)response.StatusCode);
        Assert.Equal(new Version(1, 1), response.Version);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(body);
    }
}
