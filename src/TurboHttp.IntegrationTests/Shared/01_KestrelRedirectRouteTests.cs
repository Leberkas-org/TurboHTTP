using System.Net;
using System.Text;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Tests that the Kestrel redirect fixture routes respond correctly.
/// These verify the routes themselves (server-side behavior), not the TurboHttp client.
/// Uses System.Net.HttpClient as a known-good baseline.
/// </summary>
public sealed class KestrelRedirectRouteTests : IClassFixture<KestrelFixture>
{
    private readonly HttpClient _client;
    private readonly int _port;

    public KestrelRedirectRouteTests(KestrelFixture fixture)
    {
        _port = fixture.Port;
        // Disable auto-redirect so we can inspect the raw 3xx responses
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_port}")
        };
    }

    [Theory(DisplayName = "TASK-012-001: GET /redirect/{code}/{target} returns status and Location")]
    [InlineData(301, "hello")]
    [InlineData(302, "ping")]
    [InlineData(307, "hello")]
    [InlineData(308, "hello")]
    public async Task Redirect_Code_Target_ReturnsStatusAndLocation(int code, string target)
    {
        var response = await _client.GetAsync($"/redirect/{code}/{target}");

        Assert.Equal((HttpStatusCode)code, response.StatusCode);
        Assert.Equal($"/{target}", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-002: GET /redirect/chain/3 returns 302 to /redirect/chain/2")]
    public async Task Redirect_Chain_ReturnsNextStep()
    {
        var response = await _client.GetAsync("/redirect/chain/3");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/redirect/chain/2", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-003: GET /redirect/chain/1 redirects to /hello")]
    public async Task Redirect_Chain_FinalStep_RedirectsToHello()
    {
        var response = await _client.GetAsync("/redirect/chain/1");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/hello", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-004: GET /redirect/loop returns 302 to itself")]
    public async Task Redirect_Loop_RedirectsToSelf()
    {
        var response = await _client.GetAsync("/redirect/loop");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/redirect/loop", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-005: GET /redirect/relative returns relative Location")]
    public async Task Redirect_Relative_ReturnsRelativeLocation()
    {
        var response = await _client.GetAsync("/redirect/relative");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("hello", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-006: GET /redirect/cross-scheme returns HTTP downgrade Location")]
    public async Task Redirect_CrossScheme_ReturnsHttpLocation()
    {
        var response = await _client.GetAsync("/redirect/cross-scheme");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location?.OriginalString;
        Assert.NotNull(location);
        Assert.StartsWith("http://127.0.0.1:", location);
        Assert.EndsWith("/hello", location);
    }

    [Fact(DisplayName = "TASK-012-007: POST /redirect/307 returns 307 with Location to /echo")]
    public async Task Redirect_307_PreservesMethodRedirectsToEcho()
    {
        var content = new StringContent("test-body", Encoding.UTF8, "text/plain");
        var response = await _client.PostAsync("/redirect/307", content);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("/echo", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-008: POST /redirect/303 returns 303 with Location to /hello")]
    public async Task Redirect_303_ChangesToGetRedirectsToHello()
    {
        var content = new StringContent("test-body", Encoding.UTF8, "text/plain");
        var response = await _client.PostAsync("/redirect/303", content);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/hello", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-009: GET /redirect/chain with auto-redirect follows to /hello")]
    public async Task Redirect_Chain_FullFollow_ReachesHello()
    {
        // Use a client that DOES follow redirects
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_port}")
        };

        var response = await client.GetAsync("/redirect/chain/3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }
}

/// <summary>
/// Tests the same redirect routes on the HTTP/2 (h2c) fixture.
/// </summary>
public sealed class KestrelH2RedirectRouteTests : IClassFixture<KestrelH2Fixture>
{
    private readonly HttpClient _client;
    private readonly int _port;

    public KestrelH2RedirectRouteTests(KestrelH2Fixture fixture)
    {
        _port = fixture.Port;
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_port}"),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
    }

    [Theory(DisplayName = "TASK-012-H2-001: GET /redirect/{code}/{target} returns status and Location over H2")]
    [InlineData(301, "hello")]
    [InlineData(302, "ping")]
    [InlineData(307, "hello")]
    [InlineData(308, "hello")]
    public async Task H2_Redirect_Code_Target_ReturnsStatusAndLocation(int code, string target)
    {
        var response = await _client.GetAsync($"/redirect/{code}/{target}");

        Assert.Equal((HttpStatusCode)code, response.StatusCode);
        Assert.Equal($"/{target}", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-H2-002: GET /redirect/chain/3 returns 302 over H2")]
    public async Task H2_Redirect_Chain_ReturnsNextStep()
    {
        var response = await _client.GetAsync("/redirect/chain/3");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/redirect/chain/2", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-H2-003: GET /redirect/loop returns 302 to itself over H2")]
    public async Task H2_Redirect_Loop_RedirectsToSelf()
    {
        var response = await _client.GetAsync("/redirect/loop");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/redirect/loop", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-H2-004: POST /redirect/307 returns 307 over H2")]
    public async Task H2_Redirect_307_PreservesMethod()
    {
        var content = new StringContent("test-body", Encoding.UTF8, "text/plain");
        var response = await _client.PostAsync("/redirect/307", content);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("/echo", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-H2-005: POST /redirect/303 returns 303 over H2")]
    public async Task H2_Redirect_303_ChangesToGet()
    {
        var content = new StringContent("test-body", Encoding.UTF8, "text/plain");
        var response = await _client.PostAsync("/redirect/303", content);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/hello", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-H2-006: GET /redirect/relative returns relative Location over H2")]
    public async Task H2_Redirect_Relative()
    {
        var response = await _client.GetAsync("/redirect/relative");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("hello", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "TASK-012-H2-007: GET /redirect/cross-scheme returns downgrade Location over H2")]
    public async Task H2_Redirect_CrossScheme()
    {
        var response = await _client.GetAsync("/redirect/cross-scheme");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location?.OriginalString;
        Assert.NotNull(location);
        Assert.StartsWith("http://", location);
        Assert.EndsWith("/hello", location);
    }
}
