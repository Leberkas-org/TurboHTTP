using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H10;

[Collection("H10")]
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
            new Version(1, 0),
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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Get_should_return_json_body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            cts.Token);

        var body = await response.Content.ReadAsStringAsync(cts.Token);
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Fact(Timeout = 30000)]
    public async Task Post_should_echo_request_body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var payload = "HTTP/1.0 smoke test payload";
        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await _helper!.Client.SendAsync(request, cts.Token);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Theory(Timeout = 30000)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Status_code_should_match_requested_code(int expectedCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/status/{expectedCode}"),
            cts.Token);

        Assert.Equal((HttpStatusCode)expectedCode, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Headers_should_be_forwarded_to_server()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Custom-Test", "turbohttp-h10");

        var response = await _helper!.Client.SendAsync(request, cts.Token);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        var value = headers.GetProperty("X-Custom-Test");
        var headerValue = value.ValueKind == System.Text.Json.JsonValueKind.Array
            ? value[0].GetString()
            : value.GetString();
        Assert.Equal("turbohttp-h10", headerValue);
    }

    [Fact(Timeout = 30000)]
    public async Task Bytes_endpoint_should_return_correct_length()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/bytes/512"),
            cts.Token);

        var content = await response.Content.ReadAsByteArrayAsync(cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(512, content.Length);
    }
}
