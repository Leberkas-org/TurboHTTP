using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H11;

[Collection("H11")]
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
    public async Task Encoding_should_handle_identity_encoding()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/get");
        request.Headers.Add("Accept-Encoding", "identity");

        var response = await _helper!.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Fact(Timeout = 15000)]
    public async Task Encoding_should_decompress_after_keep_alive_reuse()
    {
        var ct = TestContext.Current.CancellationToken;

        var r1 = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), ct);
        var b1 = await r1.Content.ReadAsStringAsync(ct);
        var j1 = JsonDocument.Parse(b1);
        Assert.True(j1.RootElement.GetProperty("gzipped").GetBoolean());

        var r2 = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/deflate"), ct);
        var b2 = await r2.Content.ReadAsStringAsync(ct);
        var j2 = JsonDocument.Parse(b2);
        Assert.True(j2.RootElement.GetProperty("deflated").GetBoolean());
    }
}
