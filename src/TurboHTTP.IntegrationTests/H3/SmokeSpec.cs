using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class SmokeSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public SmokeSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        QuicAvailability.SkipIfUnavailable();
        _helper = ClientHelper.CreateClient(_server.HttpsPort, new Version(3, 0), scheme: "https", system: _systemFixture.System);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Basic_get_should_return_200_with_body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }

    [Fact(Timeout = 20000)]
    public async Task Post_should_echo_request_body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var payload = "HTTP/3 smoke test payload";
        var request = new HttpRequestMessage(HttpMethod.Post, "/echo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    [Theory(Timeout = 20000)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Status_code_should_match_requested_code(int expectedCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{expectedCode}");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)expectedCode, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Custom_headers_should_round_trip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers/echo");
        request.Headers.TryAddWithoutValidation("X-Smoke-Test", "h3-value");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Smoke-Test", out var values));
        Assert.Equal("h3-value", values!.Single());
    }
}
