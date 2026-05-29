using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class ParameterBindingSpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task Route_param_should_bind_int_from_path()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/users/42"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(42, json.RootElement.GetProperty("id").GetInt32());
    }

    [Fact(Timeout = 15000)]
    public async Task Query_string_should_bind_string_param()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/search?q=turbohttp"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("turbohttp", json.RootElement.GetProperty("query").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Multiple_query_params_should_bind()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/paged?q=test&page=3"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("test", json.RootElement.GetProperty("query").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("page").GetInt32());
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_bind_from_request_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri($"http://127.0.0.1:{server.Port}/with-header"));
        request.Headers.Add("X-Tenant", "acme-corp");

        var response = await _client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("acme-corp", json.RootElement.GetProperty("tenant").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Optional_param_should_use_default_when_missing()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/optional"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("default", json.RootElement.GetProperty("name").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Optional_param_should_use_provided_value()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/optional?name=jan"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("jan", json.RootElement.GetProperty("name").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Multiple_route_params_should_bind()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/items/electronics/99"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("electronics", json.RootElement.GetProperty("category").GetString());
        Assert.Equal(99, json.RootElement.GetProperty("id").GetInt32());
    }
}
