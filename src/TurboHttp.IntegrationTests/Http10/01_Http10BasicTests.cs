using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// Integration tests for Http10Engine basic RFC 1945 compliance.
/// Verifies GET, HEAD, POST, PUT, DELETE, status codes, large bodies,
/// custom headers, multi-value headers, and empty body over HTTP/1.0.
/// Uses System.Net.HttpClient with Version10 against a real Kestrel server.
/// </summary>
public sealed class Http10BasicTests : IClassFixture<KestrelFixture>
{
    private readonly HttpClient _client;

    public Http10BasicTests(KestrelFixture fixture)
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{fixture.Port}"),
            DefaultRequestVersion = HttpVersion.Version10,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
    }

    [Fact(DisplayName = "TASK-018-001: GET /hello returns 200 with body over HTTP/1.0")]
    public async Task Get_Hello_Returns200WithBody()
    {
        var response = await _client.GetAsync("/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "TASK-018-002: HEAD /hello returns 200 with no body over HTTP/1.0")]
    public async Task Head_Hello_Returns200WithNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "/hello")
        {
            Version = HttpVersion.Version10,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "TASK-018-003: POST /echo returns echoed body over HTTP/1.0")]
    public async Task Post_Echo_ReturnsEchoedBody()
    {
        var content = new StringContent("hello-http10", Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Post, "/echo")
        {
            Version = HttpVersion.Version10,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = content
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello-http10", body);
    }

    [Fact(DisplayName = "TASK-018-004: PUT /echo returns echoed body over HTTP/1.0")]
    public async Task Put_Echo_ReturnsEchoedBody()
    {
        var content = new StringContent("put-body", Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Put, "/echo")
        {
            Version = HttpVersion.Version10,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = content
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("put-body", body);
    }

    [Fact(DisplayName = "TASK-018-005: DELETE /any returns method name over HTTP/1.0")]
    public async Task Delete_Any_ReturnsMethodName()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/any")
        {
            Version = HttpVersion.Version10,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("DELETE", body);
    }

    [Theory(DisplayName = "TASK-018-006: GET /status/{code} returns correct status over HTTP/1.0")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Get_Status_ReturnsCorrectStatusCode(int code)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{code}")
        {
            Version = HttpVersion.Version10,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        var response = await _client.SendAsync(request);

        Assert.Equal((HttpStatusCode)code, response.StatusCode);
    }

    [Fact(DisplayName = "TASK-018-007: GET /large/100 returns 100KB body over HTTP/1.0")]
    public async Task Get_Large_Returns100KbBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/large/100")
        {
            Version = HttpVersion.Version10,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(100 * 1024, body.Length);
        Assert.All(body, b => Assert.Equal((byte)'A', b));
    }

    [Fact(DisplayName = "TASK-018-008: GET /headers/echo round-trips custom headers over HTTP/1.0")]
    public async Task Get_HeadersEcho_RoundTripsCustomHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers/echo")
        {
            Version = HttpVersion.Version10,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.Add("X-Custom-One", "value1");
        request.Headers.Add("X-Custom-Two", "value2");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("value1", response.Headers.GetValues("X-Custom-One"));
        Assert.Contains("value2", response.Headers.GetValues("X-Custom-Two"));
    }

    [Fact(DisplayName = "TASK-018-009: GET /multiheader returns multi-value headers over HTTP/1.0")]
    public async Task Get_MultiHeader_ReturnsMultipleValues()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/multiheader")
        {
            Version = HttpVersion.Version10,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var values = response.Headers.GetValues("X-Value").ToList();
        Assert.Contains("alpha", values);
        Assert.Contains("beta", values);
    }

    [Fact(DisplayName = "TASK-018-010: GET /empty-cl returns 200 with empty body over HTTP/1.0")]
    public async Task Get_EmptyCl_ReturnsEmptyBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/empty-cl")
        {
            Version = HttpVersion.Version10,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }
}
