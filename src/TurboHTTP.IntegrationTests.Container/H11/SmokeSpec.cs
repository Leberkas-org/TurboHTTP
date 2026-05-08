using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H11;

[Collection("H11")]
public sealed class SmokeSpec : IAsyncLifetime
{
    private readonly ServerContainerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public SmokeSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        if (!_server.IsDockerAvailable)
        {
            Assert.Skip("Docker is not available.");
        }

        _helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 1),
            system: _systemFixture.System);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Get_should_return_200()
    {
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Get_should_return_json_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Fact(Timeout = 30000)]
    public async Task Post_should_echo_request_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = """{"key":"value"}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Fact(Timeout = 30000)]
    public async Task Status_endpoint_should_return_requested_status_code()
    {
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/status/418"),
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)418, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Headers_should_be_forwarded_to_server()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Custom-Test", "turbohttp-v2");

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        var value = headers.GetProperty("X-Custom-Test");
        var headerValue = value.ValueKind == System.Text.Json.JsonValueKind.Array
            ? value[0].GetString()
            : value.GetString();
        Assert.Equal("turbohttp-v2", headerValue);
    }

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_return_redirect_status()
    {
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/1"),
            TestContext.Current.CancellationToken);

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Found or HttpStatusCode.Redirect,
            $"Expected OK or redirect status, got {response.StatusCode}");
    }

    [Fact(Timeout = 30000)]
    public async Task Gzip_response_should_be_decompressed()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"),
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("gzipped").GetBoolean());
    }

    [Fact(Timeout = 30000)]
    public async Task Bytes_endpoint_should_return_correct_length()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/bytes/1024"),
            ct);

        var content = await response.Content.ReadAsByteArrayAsync(ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1024, content.Length);
    }
}
