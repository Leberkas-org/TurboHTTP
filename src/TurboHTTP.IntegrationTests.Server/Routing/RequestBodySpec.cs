using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class RequestBodySpec(ActorSystemFixture systemFixture) : ServerSpecBase(systemFixture)
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
        app.MapPost("/echo-body", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            return Results.Ok(new { body });
        });

        app.MapPost("/echo-json", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var raw = await reader.ReadToEndAsync();
            var parsed = JsonDocument.Parse(raw);
            return Results.Ok(parsed.RootElement);
        });

        app.MapPost("/form", async (HttpContext ctx) =>
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
