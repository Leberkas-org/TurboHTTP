using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.Tests.Shared;
using TurboHTTP.IntegrationTests.Client.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
public sealed class SmokeSpec : IntegrationSpecBase
{
    public SmokeSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H3, Tls: true);

    [Fact(Timeout = 20000)]
    public async Task Get_should_return_200()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Get_should_return_json_body()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Fact(Timeout = 20000)]
    public async Task Post_should_echo_request_body()
    {
        var payload = """{"test":"h3"}""";
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

    [Theory(Timeout = 20000)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Status_code_should_match_requested_code(int expectedCode)
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/status/{expectedCode}"),
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)expectedCode, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Headers_should_be_forwarded_to_server()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Custom-Test", "turbohttp-h3");

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Equal("turbohttp-h3", headers.GetHeaderValue("X-Custom-Test"));
    }

    [Fact(Timeout = 20000)]
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

    [Fact(Timeout = 20000)]
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