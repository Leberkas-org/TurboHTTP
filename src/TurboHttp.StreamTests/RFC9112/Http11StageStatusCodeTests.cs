using System.Net;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.RFC9112;

/// <summary>
/// Tests HTTP/1.1 response status code parsing via the full engine per RFC 9110.
/// Verifies that 1xx, 2xx, 3xx, 4xx, and 5xx status codes are correctly decoded and exposed on the response.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http11DecoderStage"/>.
/// RFC 9110 §15.1: HTTP status code classes and their required handling by clients.
/// </remarks>
public sealed class Http11StageStatusCodeTests : EngineTestBase
{
    private static Http11Engine Engine => new();

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.1-11SC-001: 200 OK → StatusCode=200")]
    public async Task Should_Return200_WhenServerRespondsOk()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/ok");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"u8.ToArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(new Version(1, 1), response.Version);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ok", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.1-11SC-002: 301 Moved Permanently → StatusCode=301, Location header present")]
    public async Task Should_Return301WithLocationHeader_WhenMovedPermanently()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 301 Moved Permanently\r\nLocation: http://example.com/new\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(301, (int)response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(new Uri("http://example.com/new"), response.Headers.Location);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.1-11SC-003: 404 Not Found → StatusCode=404")]
    public async Task Should_Return404_WhenResourceNotFound()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 404 Not Found\r\nContent-Length: 9\r\n\r\nnot found"u8.ToArray());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("not found", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.1-11SC-004: 500 Internal Server Error → StatusCode=500")]
    public async Task Should_Return500_WhenInternalServerError()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/error");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 5\r\n\r\nerror"u8.ToArray());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(500, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("error", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9110-15.1-11SC-005: 204 No Content → StatusCode=204, no body")]
    public async Task Should_Return204WithEmptyBody_WhenNoContent()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(204, (int)response.StatusCode);
        Assert.Equal(new Version(1, 1), response.Version);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }
}
