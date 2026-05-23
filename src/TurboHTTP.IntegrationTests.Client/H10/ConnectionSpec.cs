using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H10;

[Collection("H10")]
public sealed class ConnectionSpec : IntegrationSpecBase
{
    public ConnectionSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H10, Tls: false);

    [Fact(Timeout = 15000)]
    public async Task Connection_should_complete_single_request_response_cycle()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.False(string.IsNullOrEmpty(body));
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_handle_sequential_requests()
    {
        for (var i = 0; i < 5; i++)
        {
            var response = await Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_return_body_for_get_request()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("url", out _));
        Assert.True(json.RootElement.TryGetProperty("headers", out _));
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_echo_post_body()
    {
        var payload = """{"protocol":"HTTP/1.0","test":"connection"}""";
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
        var payload = "PUT body test";
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
        var payload = "PATCH body test";
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
}