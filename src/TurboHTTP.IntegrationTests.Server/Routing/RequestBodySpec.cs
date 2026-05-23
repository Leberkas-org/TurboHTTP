using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class RequestBodySpec : ServerSpecBase
{
    protected override void ConfigureServer(IServiceCollection services, ushort port)
    {
        services.AddTurboKestrel(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
        });
    }

    protected override void ConfigureRoutes(TurboRouteTable routeTable)
    {
        routeTable.Add(HttpMethod.Post, "/echo-body", async (TurboHttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            return Results.Ok(new { body });
        });

        routeTable.Add(HttpMethod.Post, "/echo-json", async (TurboHttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var raw = await reader.ReadToEndAsync();
            var parsed = JsonDocument.Parse(raw);
            return Results.Ok(parsed.RootElement);
        });

        routeTable.Add(HttpMethod.Post, "/form", async (TurboHttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var name = form["name"].ToString();
            var age = form["age"].ToString();
            return Results.Ok(new { name, age });
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Post_should_receive_text_body()
    {
        var response = await Client.PostAsync(
            new Uri($"http://127.0.0.1:{Port}/echo-body"),
            new StringContent("hello server", Encoding.UTF8, "text/plain"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("hello server", json.RootElement.GetProperty("body").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Post_should_receive_json_body()
    {
        var jsonContent = new StringContent(
            "{\"name\":\"turbo\",\"version\":2}",
            Encoding.UTF8,
            "application/json");

        var response = await Client.PostAsync(
            new Uri($"http://127.0.0.1:{Port}/echo-json"),
            jsonContent,
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("turbo", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("version").GetInt32());
    }

    [Fact(Timeout = 15000)]
    public async Task Post_should_receive_form_encoded_body()
    {
        var formData = new Dictionary<string, string>
        {
            { "name", "jan" },
            { "age", "30" }
        };
        var content = new FormUrlEncodedContent(formData);

        var response = await Client.PostAsync(
            new Uri($"http://127.0.0.1:{Port}/form"),
            content,
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("jan", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("30", json.RootElement.GetProperty("age").GetString());
    }
}
