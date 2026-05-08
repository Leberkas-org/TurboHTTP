using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H11;

[Collection("H11")]
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
            new Version(1, 1),
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
    public async Task Connection_should_allow_sequential_requests_on_same_client()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 10; i++)
        {
            var response = await _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_reuse_across_different_endpoints()
    {
        var ct = TestContext.Current.CancellationToken;
        var endpoints = new[] { "/get", "/headers", "/bytes/64", "/get" };

        foreach (var endpoint in endpoints)
        {
            var response = await _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, endpoint), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_echo_post_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = """{"protocol":"HTTP/1.1","test":"connection"}""";
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
        var payload = "PUT body HTTP/1.1";
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
        var payload = "PATCH body HTTP/1.1";
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

    [Fact(Timeout = 15000)]
    public async Task Connection_should_alternate_get_and_post_sequentially()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 5; i++)
        {
            var getResponse = await _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct);
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var postResponse = await _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/post")
                {
                    Content = new StringContent($"iteration-{i}")
                }, ct);
            Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        }
    }
}
