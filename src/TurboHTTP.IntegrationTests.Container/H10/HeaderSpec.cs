using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H10;

[Collection("H10")]
public sealed class HeaderSpec : IAsyncLifetime
{
    private readonly ServerContainerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public HeaderSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
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

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_custom_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Custom-Test", "turbohttp-h10");

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        var value = headers.GetProperty("X-Custom-Test");
        var headerValue = value.ValueKind == JsonValueKind.Array
            ? value[0].GetString()
            : value.GetString();

        Assert.Equal("turbohttp-h10", headerValue);
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_multiple_custom_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-First", "one");
        request.Headers.Add("X-Second", "two");
        request.Headers.Add("X-Third", "three");

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");

        Assert.Equal("one", GetHeaderValue(headers, "X-First"));
        Assert.Equal("two", GetHeaderValue(headers, "X-Second"));
        Assert.Equal("three", GetHeaderValue(headers, "X-Third"));
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_user_agent()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.UserAgent.ParseAdd("TurboHTTP/1.0 IntegrationTest");

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        var ua = GetHeaderValue(headers, "User-Agent");

        Assert.Contains("TurboHTTP/1.0", ua);
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_receive_response_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/response-headers?X-Server-Custom=test-value"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Server-Custom", out var values));
        Assert.Contains("test-value", values);
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_preserve_header_with_special_characters()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Special", "value with spaces and (parens)");

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Equal("value with spaces and (parens)", GetHeaderValue(headers, "X-Special"));
    }

    private static string? GetHeaderValue(JsonElement headers, string name)
    {
        if (!headers.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Array
            ? value[0].GetString()
            : value.GetString();
    }
}
