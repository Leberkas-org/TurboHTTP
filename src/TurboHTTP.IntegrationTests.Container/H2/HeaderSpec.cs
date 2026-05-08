using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H2;

[Collection("H2")]
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

        if (_server.HttpsPort == 0)
        {
            Assert.Skip("Nginx TLS proxy is not available.");
        }

        _helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(2, 0),
            scheme: "https",
            system: _systemFixture.System,
            host: "localhost");
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
        request.Headers.Add("X-Custom-Test", "turbohttp-h2");

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Equal("turbohttp-h2", GetHeaderValue(headers, "X-Custom-Test"));
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_many_custom_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");

        for (var i = 0; i < 20; i++)
        {
            request.Headers.Add($"X-Header-{i}", $"value-{i}");
        }

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal($"value-{i}", GetHeaderValue(headers, $"X-Header-{i}"));
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_user_agent()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.UserAgent.ParseAdd("TurboHTTP/2.0 IntegrationTest");

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Contains("TurboHTTP/2.0", GetHeaderValue(headers, "User-Agent"));
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_receive_response_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/response-headers?X-Server-Custom=h2-value"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Server-Custom", out var values));
        Assert.Contains("h2-value", values);
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_handle_large_header_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var largeValue = new string('A', 1024);
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Large-Header", largeValue);

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Equal(largeValue, GetHeaderValue(headers, "X-Large-Header"));
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_preserve_headers_across_multiplexed_requests()
    {
        var ct = TestContext.Current.CancellationToken;

        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
            request.Headers.Add("X-Request-Id", $"req-{i}");

            var response = await _helper!.Client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var json = JsonDocument.Parse(body);

            var headers = json.RootElement.GetProperty("headers");
            return GetHeaderValue(headers, "X-Request-Id");
        });

        var results = await Task.WhenAll(tasks);
        var sorted = results.Order().ToArray();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal($"req-{i}", sorted[i]);
        }
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
