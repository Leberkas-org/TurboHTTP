using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class ParameterBindingSpec : ServerSpecBase
{
    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        builder.Host.UseTurboHttp(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/users/{id:int}", (int id) =>
            Results.Ok(new { id }));

        app.MapGet("/search", (string q) =>
            Results.Ok(new { query = q }));

        app.MapGet("/paged", (string q, int page) =>
            Results.Ok(new { query = q, page }));

        app.MapGet("/with-header",
            ([FromHeader(Name = "X-Tenant")] string tenant) =>
                Results.Ok(new { tenant }));

        app.MapGet("/optional", (string? name) =>
            Results.Ok(new { name = name ?? "default" }));

        app.MapGet("/items/{category}/{id}", (string category, int id) =>
            Results.Ok(new { category, id }));
    }

    [Fact(Timeout = 15000)]
    public async Task Route_param_should_bind_int_from_path()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/users/42"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(42, json.RootElement.GetProperty("id").GetInt32());
    }

    [Fact(Timeout = 15000)]
    public async Task Query_string_should_bind_string_param()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/search?q=turbohttp"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("turbohttp", json.RootElement.GetProperty("query").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Multiple_query_params_should_bind()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/paged?q=test&page=3"), CancellationToken);

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
            new Uri($"http://127.0.0.1:{Port}/with-header"));
        request.Headers.Add("X-Tenant", "acme-corp");

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("acme-corp", json.RootElement.GetProperty("tenant").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Optional_param_should_use_default_when_missing()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/optional"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("default", json.RootElement.GetProperty("name").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Optional_param_should_use_provided_value()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/optional?name=jan"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("jan", json.RootElement.GetProperty("name").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Multiple_route_params_should_bind()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/items/electronics/99"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("electronics", json.RootElement.GetProperty("category").GetString());
        Assert.Equal(99, json.RootElement.GetProperty("id").GetInt32());
    }
}
