using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H10;

[Collection("H10")]
public sealed class SmokeSpec : IntegrationSpecBase
{
    public SmokeSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H10, tls: false);

    [Fact(Timeout = 15000)]
    public async Task Get_should_return_200()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Get_should_return_json_body()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Fact(Timeout = 15000)]
    public async Task Post_should_echo_request_body()
    {
        var payload = "HTTP/1.0 smoke test payload";
        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Theory(Timeout = 15000)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Status_code_should_match_requested_code(int expectedCode)
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/status/{expectedCode}"),
            CancellationToken);

        Assert.Equal((HttpStatusCode)expectedCode, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Headers_should_be_forwarded_to_server()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Custom-Test", "turbohttp-h10");

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        var headerValue = headers.GetHeaderValue("X-Custom-Test");
        Assert.Equal("turbohttp-h10", headerValue);
    }

    [Fact(Timeout = 15000)]
    public async Task Bytes_endpoint_should_return_correct_length()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/bytes/512"),
            CancellationToken);

        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(512, content.Length);
    }
}