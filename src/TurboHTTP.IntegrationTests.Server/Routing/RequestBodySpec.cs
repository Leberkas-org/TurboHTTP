using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class RequestBodySpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 15000)]
    public async Task Post_should_receive_text_body()
    {
        var response = await _client.PostAsync(
            new Uri($"http://127.0.0.1:{server.Port}/echo-body"),
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

        var response = await _client.PostAsync(
            new Uri($"http://127.0.0.1:{server.Port}/echo-json"),
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

        var response = await _client.PostAsync(
            new Uri($"http://127.0.0.1:{server.Port}/form"),
            content,
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("jan", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("30", json.RootElement.GetProperty("age").GetString());
    }
}
