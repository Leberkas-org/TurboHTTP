using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H2;

[Collection("H2")]
public sealed class TransferSpec : IAsyncLifetime
{
    private readonly ServerContainerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public TransferSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
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

    [Theory(Timeout = 15000)]
    [InlineData(128)]
    [InlineData(1024)]
    [InlineData(8192)]
    [InlineData(65536)]
    public async Task Transfer_should_receive_binary_body_of_exact_size(int size)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/bytes/{size}"), ct);

        var content = await response.Content.ReadAsByteArrayAsync(ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(size, content.Length);
    }

    [Fact(Timeout = 30000)]
    public async Task Transfer_should_receive_large_100kb_body()
    {
        var ct = TestContext.Current.CancellationToken;
        const int size = 100 * 1024;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/bytes/{size}"), ct);

        var content = await response.Content.ReadAsByteArrayAsync(ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(size, content.Length);
    }

    [Fact(Timeout = 15000)]
    public async Task Transfer_should_handle_empty_body_for_204()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/status/204"), ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [InlineData(200, HttpStatusCode.OK)]
    [InlineData(201, HttpStatusCode.Created)]
    [InlineData(204, HttpStatusCode.NoContent)]
    [InlineData(400, HttpStatusCode.BadRequest)]
    [InlineData(401, HttpStatusCode.Unauthorized)]
    [InlineData(403, HttpStatusCode.Forbidden)]
    [InlineData(404, HttpStatusCode.NotFound)]
    [InlineData(500, HttpStatusCode.InternalServerError)]
    [InlineData(502, HttpStatusCode.BadGateway)]
    [InlineData(503, HttpStatusCode.ServiceUnavailable)]
    public async Task Transfer_should_return_correct_status_code(int code, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/status/{code}"), ct);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Transfer_should_echo_large_post_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = new string('X', 8192);
        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Fact(Timeout = 30000)]
    public async Task Transfer_should_echo_60kb_binary_post_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = new byte[60 * 1024];
        Random.Shared.NextBytes(payload);

        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var response = await _helper!.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Transfer_should_receive_streaming_response()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/stream/5"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, lines.Length);
    }

    [Fact(Timeout = 30000)]
    public async Task Transfer_should_handle_concurrent_large_bodies()
    {
        var ct = TestContext.Current.CancellationToken;
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/bytes/16384"), ct));

        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsByteArrayAsync(ct);
            Assert.Equal(16384, content.Length);
        }
    }
}
