using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H2;

[Collection("H2")]
public sealed class ConnectionSpec : IntegrationSpecBase
{
    public ConnectionSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H2, tls: true);

    [Fact(Timeout = 15000)]
    public async Task Connection_should_reuse_for_sequential_requests()
    {
        for (var i = 0; i < 10; i++)
        {
            var response = await Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_multiplex_concurrent_requests()
    {
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_reuse_across_different_endpoints()
    {
        var endpoints = new[] { "/get", "/headers", "/bytes/64", "/status/200", "/get" };

        foreach (var endpoint in endpoints)
        {
            var response = await Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, endpoint), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_echo_post_body()
    {
        var payload = """{"protocol":"HTTP/2","test":"connection"}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_echo_put_body()
    {
        var payload = "PUT body HTTP/2";
        var request = new HttpRequestMessage(HttpMethod.Put, "/put")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_echo_patch_body()
    {
        var payload = "PATCH body HTTP/2";
        var request = new HttpRequestMessage(HttpMethod.Patch, "/patch")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_handle_delete_method()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/delete"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_alternate_get_and_post_sequentially()
    {
        for (var i = 0; i < 5; i++)
        {
            var getResponse = await Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var postResponse = await Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/post")
                {
                    Content = new StringContent($"iteration-{i}")
                }, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        }
    }
}