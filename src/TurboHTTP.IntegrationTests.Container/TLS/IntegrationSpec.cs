using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.TLS;

[Collection("TLS")]
public sealed class IntegrationSpec : IAsyncLifetime
{
    private readonly ServerContainerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public IntegrationSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
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
            new Version(1, 1),
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
    public async Task Tls_should_complete_get_over_https()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Fact(Timeout = 15000)]
    public async Task Tls_should_echo_post_body_over_https()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = """{"tls":"test"}""";
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

    [Fact(Timeout = 15000)]
    public async Task Tls_should_forward_custom_headers_over_https()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Tls-Test", "secure-header");

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        var value = headers.GetProperty("X-Tls-Test");
        var headerValue = value.ValueKind == JsonValueKind.Array
            ? value[0].GetString()
            : value.GetString();

        Assert.Equal("secure-header", headerValue);
    }

    [Fact(Timeout = 15000)]
    public async Task Tls_should_decompress_gzip_over_https()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("gzipped").GetBoolean());
    }

    [Theory(Timeout = 30000)]
    [InlineData(1024)]
    [InlineData(65536)]
    [InlineData(102400)]
    public async Task Tls_should_transfer_large_body_over_https(int size)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/bytes/{size}"), ct);

        var content = await response.Content.ReadAsByteArrayAsync(ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(size, content.Length);
    }

    [Fact(Timeout = 15000)]
    public async Task Tls_should_reuse_connection_for_sequential_requests()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 5; i++)
        {
            var response = await _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Tls_should_reuse_across_different_endpoints()
    {
        var ct = TestContext.Current.CancellationToken;
        var endpoints = new[] { "/get", "/headers", "/bytes/128", "/status/200" };

        foreach (var endpoint in endpoints)
        {
            var response = await _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, endpoint), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Tls_should_receive_streaming_response_over_https()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/stream/3"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, lines.Length);
    }

    [Theory(Timeout = 15000)]
    [InlineData(200, HttpStatusCode.OK)]
    [InlineData(201, HttpStatusCode.Created)]
    [InlineData(400, HttpStatusCode.BadRequest)]
    [InlineData(404, HttpStatusCode.NotFound)]
    [InlineData(500, HttpStatusCode.InternalServerError)]
    public async Task Tls_should_return_correct_status_code_over_https(int code, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/status/{code}"), ct);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Tls_should_handle_concurrent_requests_over_https()
    {
        var ct = TestContext.Current.CancellationToken;
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
