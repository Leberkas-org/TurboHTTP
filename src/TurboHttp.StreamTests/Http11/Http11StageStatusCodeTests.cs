using System.Net;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http11;

/// <summary>
/// RFC 9110 §15 — HTTP/1.1 Status Code tests through Akka.Streams stages.
/// Validates that various HTTP status codes are correctly parsed and surfaced
/// in the HttpResponseMessage after flowing through the Http11Engine pipeline.
/// </summary>
public sealed class Http11StageStatusCodeTests : EngineTestBase
{
    private static Http11Engine Engine => new();

    // ── 11SC-001: 200 OK → StatusCode=200 ────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9110-§15-11SC-001: 200 OK → StatusCode=200")]
    public async Task _11SC_001_200_OK()
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

    // ── 11SC-002: 301 Moved Permanently → StatusCode=301, Location header present ─

    [Fact(Timeout = 10_000, DisplayName = "RFC-9110-§15-11SC-002: 301 Moved Permanently → StatusCode=301, Location header present")]
    public async Task _11SC_002_301_Moved_Permanently_Location_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 301 Moved Permanently\r\nLocation: http://example.com/new\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(301, (int)response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(new Uri("http://example.com/new"), response.Headers.Location);
    }

    // ── 11SC-003: 404 Not Found → StatusCode=404 ─────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9110-§15-11SC-003: 404 Not Found → StatusCode=404")]
    public async Task _11SC_003_404_Not_Found()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 404 Not Found\r\nContent-Length: 9\r\n\r\nnot found"u8.ToArray());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("not found", body);
    }

    // ── 11SC-004: 500 Internal Server Error → StatusCode=500 ──────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9110-§15-11SC-004: 500 Internal Server Error → StatusCode=500")]
    public async Task _11SC_004_500_Internal_Server_Error()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/error");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 5\r\n\r\nerror"u8.ToArray());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(500, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("error", body);
    }

    // ── 11SC-005: 204 No Content → StatusCode=204, no body ────────────────────

    [Fact(Timeout = 10_000, DisplayName = "RFC-9110-§15-11SC-005: 204 No Content → StatusCode=204, no body")]
    public async Task _11SC_005_204_No_Content_Empty_Body()
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
