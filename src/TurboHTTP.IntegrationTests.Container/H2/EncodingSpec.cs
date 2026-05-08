using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H2;

[Collection("H2")]
public sealed class EncodingSpec : IAsyncLifetime
{
    private readonly ServerContainerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public EncodingSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
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
    public async Task Encoding_should_decompress_gzip_response()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("gzipped").GetBoolean());
    }

    [Fact(Timeout = 15000)]
    public async Task Encoding_should_decompress_deflate_response()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/deflate"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("deflated").GetBoolean());
    }

    [Fact(Timeout = 15000)]
    public async Task Encoding_should_negotiate_accept_encoding()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/get");
        request.Headers.Add("Accept-Encoding", "gzip, deflate");

        var response = await _helper!.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.False(string.IsNullOrEmpty(body));
    }

    [Fact(Timeout = 15000)]
    public async Task Encoding_should_decompress_sequentially_on_same_connection()
    {
        var ct = TestContext.Current.CancellationToken;

        var r1 = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), ct);
        var b1 = await r1.Content.ReadAsStringAsync(ct);
        Assert.True(JsonDocument.Parse(b1).RootElement.GetProperty("gzipped").GetBoolean());

        var r2 = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/deflate"), ct);
        var b2 = await r2.Content.ReadAsStringAsync(ct);
        Assert.True(JsonDocument.Parse(b2).RootElement.GetProperty("deflated").GetBoolean());

        var r3 = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), ct);
        var b3 = await r3.Content.ReadAsStringAsync(ct);
        Assert.True(JsonDocument.Parse(b3).RootElement.GetProperty("gzipped").GetBoolean());
    }
}
