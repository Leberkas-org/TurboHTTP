using System.Net;
using System.Net.Quic;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
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

        if (!QuicConnection.IsSupported)
        {
            Assert.Skip("QUIC/HTTP3 is not supported on this platform.");
        }

        if (_server.QuicPort == 0 || !_server.IsQuicAvailable)
        {
            Assert.Skip("QUIC/HTTP3 is not available on this host.");
        }

        _helper = ClientHelper.CreateClient(
            _server.QuicPort,
            new Version(3, 0),
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

    [Fact(Timeout = 20000)]
    public async Task Get_should_return_200()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
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

    [Fact(Timeout = 20000)]
    public async Task Post_should_echo_request_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var payload = """{"test":"h3"}""";
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

    [Theory(Timeout = 20000)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Status_code_should_match_requested_code(int expectedCode)
    {
        var response = await _helper!.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/status/{expectedCode}"),
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)expectedCode, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Headers_should_be_forwarded_to_server()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Custom-Test", "turbohttp-h3");

        var response = await _helper!.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        var value = headers.GetProperty("X-Custom-Test");
        var headerValue = value.ValueKind == System.Text.Json.JsonValueKind.Array
            ? value[0].GetString()
            : value.GetString();
        Assert.Equal("turbohttp-h3", headerValue);
    }

    [Fact(Timeout = 20000)]
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

    [Fact(Timeout = 20000)]
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
