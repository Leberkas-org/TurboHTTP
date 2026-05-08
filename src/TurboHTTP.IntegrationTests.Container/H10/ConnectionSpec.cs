using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H10;

[Collection("H10")]
public sealed class ConnectionSpec : IAsyncLifetime
{
    private readonly ServerContainerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public ConnectionSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        if (!_server.IsDockerAvailable)
        {
            Assert.Skip("Docker is not available.");
        }

        _helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
            system: _systemFixture.System);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_complete_single_request_response_cycle()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.False(string.IsNullOrEmpty(body));
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_handle_sequential_requests()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 5; i++)
        {
            var response = await _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_return_body_for_get_request()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("url", out _));
        Assert.True(json.RootElement.TryGetProperty("headers", out _));
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_echo_post_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = """{"protocol":"HTTP/1.0","test":"connection"}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_echo_put_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = "PUT body test";
        var request = new HttpRequestMessage(HttpMethod.Put, "/put")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_echo_patch_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = "PATCH body test";
        var request = new HttpRequestMessage(HttpMethod.Patch, "/patch")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_handle_delete_method()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/delete"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
