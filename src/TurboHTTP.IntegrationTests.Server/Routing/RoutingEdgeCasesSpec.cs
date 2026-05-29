using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class RoutingEdgeCasesSpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task Multi_method_route_should_handle_GET()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/multi"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("GET", json.RootElement.GetProperty("method").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Multi_method_route_should_handle_POST()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"http://127.0.0.1:{server.Port}/multi"))
        {
            Content = new StringContent("")
        };

        var response = await _client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("POST", json.RootElement.GetProperty("method").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Multi_method_route_should_handle_PUT()
    {
        var request = new HttpRequestMessage(HttpMethod.Put,
            new Uri($"http://127.0.0.1:{server.Port}/multi"))
        {
            Content = new StringContent("")
        };

        var response = await _client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("PUT", json.RootElement.GetProperty("method").GetString());
    }

    [Fact(Timeout = 15000)]
    public async Task Multi_method_route_should_return_404_for_unregistered_method()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete,
            new Uri($"http://127.0.0.1:{server.Port}/multi"));

        var response = await _client.SendAsync(request, CancellationToken);

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
            new Uri($"http://127.0.0.1:{server.Port}/upload"))
        {
            Content = multipart
        };

        var response = await _client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("test.txt", json.RootElement.GetProperty("fileName").GetString());
        Assert.Equal(fileBytes.Length, json.RootElement.GetProperty("size").GetInt64());
        Assert.Equal(fileContent, json.RootElement.GetProperty("content").GetString());
    }
}