using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H11;

[Collection("H11")]
public sealed class SmokeSpec : IntegrationSpecBase
{
    public SmokeSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H11, Tls: false);

    [Fact(Timeout = 30000)]
    public async Task Get_should_return_200()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Get_should_return_json_body()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Fact(Timeout = 30000)]
    public async Task Post_should_echo_request_body()
    {
        var payload = """{"key":"value"}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Fact(Timeout = 30000)]
    public async Task Status_endpoint_should_return_requested_status_code()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/status/418"),
            CancellationToken);

        Assert.Equal((HttpStatusCode)418, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Headers_should_be_forwarded_to_server()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Custom-Test", "turbohttp-v2");

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        var headerValue = headers.GetHeaderValue("X-Custom-Test");
        Assert.Equal("turbohttp-v2", headerValue);
    }

    [Fact(Timeout = 30000)]
    public async Task Redirect_should_return_redirect_status()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/redirect/1"),
            CancellationToken);

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Found or HttpStatusCode.Redirect,
            $"Expected OK or redirect status, got {response.StatusCode}");
    }

    [Fact(Timeout = 30000)]
    public async Task Gzip_response_should_be_decompressed()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"),
            CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("gzipped").GetBoolean());
    }

    [Fact(Timeout = 30000)]
    public async Task Bytes_endpoint_should_return_correct_length()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/bytes/1024"),
            CancellationToken);

        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1024, content.Length);
    }
}