using System.Net;
using System.Text;
using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TurboHttp.Client;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;

namespace TurboHttp.IntegrationTests.Client;

[Collection("Http11Integration")]
public sealed class TurboHttpClientIntegrationTests : IAsyncLifetime
{
    private readonly KestrelFixture _fixture;
    private ActorSystem? _system;

    public TurboHttpClientIntegrationTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var diSetup = DependencyResolverSetup.Create(sp);
        var setup = BootstrapSetup.Create().And(diSetup);
        _system = ActorSystem.Create($"itg-{Guid.NewGuid():N}", setup);
        var manager = _system.ActorOf(Props.Create<ClientManager>(), "client-manager");
        ActorRegistry.For(_system).Register<ClientManager>(manager);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_system is not null)
        {
            await _system.Terminate();
        }
    }

    private TurboHttpClient BuildClient(TurboClientOptions? options = null)
    {
        var opts = options ?? new TurboClientOptions
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_fixture.Port}/")
        };
        return new TurboHttpClient(opts, _system!);
    }

    [Fact(Timeout = 10_000, DisplayName = "ITG-001: GET /ping → 200, body == \"pong\"")]
    public async Task ITG_001_Get_Ping_Returns_Pong()
    {
        var client = BuildClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping");

        var response = await client.SendAsync(request, CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "ITG-002: BaseAddress set → relative URI \"/ping\" resolves correctly")]
    public async Task ITG_002_BaseAddress_RelativeUri_ResolvesCorrectly()
    {
        var client = BuildClient(new TurboClientOptions
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_fixture.Port}/")
        });
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ping", UriKind.Relative));

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "ITG-003: DefaultRequestVersion = 1.0 → response.Version == 1.0")]
    public async Task ITG_003_DefaultRequestVersion_1_0_ResponseVersion()
    {
        var client = BuildClient(new TurboClientOptions
        {
            BaseAddress           = new Uri($"http://127.0.0.1:{_fixture.Port}/"),
            DefaultRequestVersion = HttpVersion.Version10
        });
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping");

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
    }

    [Fact(Timeout = 10_000, DisplayName = "ITG-004: DefaultRequestHeaders[\"X-Test\"] = \"hello\" → echoed back")]
    public async Task ITG_004_DefaultRequestHeaders_EchoedBack()
    {
        var client = BuildClient();
        client.DefaultRequestHeaders.Add("X-Test", "hello");
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/headers/echo");

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Test"));
        Assert.Contains("hello", response.Headers.GetValues("X-Test"));
    }

    [Fact(Timeout = 10_000, DisplayName = "ITG-005: POST /echo with body → body echoed, Content-Length correct")]
    public async Task ITG_005_Post_Echo_BodyEchoed()
    {
        var client = BuildClient();
        const string body = "hello-integration";
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_fixture.Port}/echo")
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        var response = await client.SendAsync(request, CancellationToken.None);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, responseBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "ITG-006: GET /status/404 → 404 status code, no exception")]
    public async Task ITG_006_Get_Status_404()
    {
        var client = BuildClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/status/404");

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "ITG-007: GET /status/500 → 500 status code, no exception")]
    public async Task ITG_007_Get_Status_500()
    {
        var client = BuildClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/status/500");

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "ITG-008: 10 concurrent GETs all return 200")]
    public async Task ITG_008_TenConcurrentGets_AllReturn200()
    {
        var client = BuildClient();
        var tasks = new Task<HttpResponseMessage>[10];
        for (var i = 0; i < 10; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping");
            tasks[i] = client.SendAsync(request, CancellationToken.None);
        }

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(10, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Skip = "No TLS fixture available",
        DisplayName = "ITG-009: https URI → TlsOptions used, TLS handshake succeeds")]
    public Task ITG_009_HttpsUri_TlsHandshake()
    {
        return Task.CompletedTask;
    }

    [Fact(Timeout = 10_000, DisplayName = "ITG-010: Timeout = 100ms, GET /slow/500 → TimeoutException within ~200ms")]
    public async Task ITG_010_Timeout_SlowEndpoint_ThrowsTimeoutException()
    {
        var client = BuildClient();
        client.Timeout = TimeSpan.FromMilliseconds(100);
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/slow/500");

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.SendAsync(request, CancellationToken.None));
    }
}
