using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class RoutingEdgeCasesSpec : ServerSpecBase
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
        routeTable.Add("GET", "/multi", () =>
            Results.Ok(new { method = "GET" }));
        routeTable.Add("POST", "/multi", () =>
            Results.Ok(new { method = "POST" }));
        routeTable.Add("PUT", "/multi", () =>
            Results.Ok(new { method = "PUT" }));

        routeTable.Add("POST", "/upload", async (TurboHttpContext ctx) =>
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

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("test.txt", json.RootElement.GetProperty("fileName").GetString());
        Assert.Equal(fileBytes.Length, json.RootElement.GetProperty("size").GetInt64());
        Assert.Equal(fileContent, json.RootElement.GetProperty("content").GetString());
    }
}
