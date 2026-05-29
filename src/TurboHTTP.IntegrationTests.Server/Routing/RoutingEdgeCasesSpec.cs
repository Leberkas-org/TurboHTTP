using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class RoutingEdgeCasesSpec(ActorSystemFixture systemFixture) : ServerSpecBase(systemFixture)
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
        app.MapGet("/multi", () =>
            Results.Ok(new { method = "GET" }));
        app.MapPost("/multi", () =>
            Results.Ok(new { method = "POST" }));
        app.MapPut("/multi", () =>
            Results.Ok(new { method = "PUT" }));

        app.MapPost("/upload", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files.GetFile("document");
            if (file is null)
            {
                return Results.BadRequest("No file");
            }

            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync();
            return Results.Ok(new
            {
                fileName = file.FileName,
                size = file.Length,
                content
            });
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Multi_method_route_should_handle_GET()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/multi"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("GET", json.RootElement.GetProperty("method").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Multi_method_route_should_handle_POST()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"http://127.0.0.1:{Port}/multi"))
        {
            Content = new StringContent("")
        };

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("POST", json.RootElement.GetProperty("method").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Multi_method_route_should_handle_PUT()
    {
        var request = new HttpRequestMessage(HttpMethod.Put,
            new Uri($"http://127.0.0.1:{Port}/multi"))
        {
            Content = new StringContent("")
        };

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("PUT", json.RootElement.GetProperty("method").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Multi_method_route_should_return_404_for_unregistered_method()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete,
            new Uri($"http://127.0.0.1:{Port}/multi"));

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Upload_should_receive_multipart_file()
    {
        var fileContent = "Hello from uploaded file!";
        var fileBytes = Encoding.UTF8.GetBytes(fileContent);

        using var multipart = new MultipartFormDataContent();
        var fileStream = new ByteArrayContent(fileBytes);
        fileStream.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        multipart.Add(fileStream, "document", "test.txt");

        var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"http://127.0.0.1:{Port}/upload"))
        {
            Content = multipart
        };

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("test.txt", json.RootElement.GetProperty("fileName").GetString());
        Assert.Equal(fileBytes.Length, json.RootElement.GetProperty("size").GetInt64());
        Assert.Equal(fileContent, json.RootElement.GetProperty("content").GetString());
    }
}