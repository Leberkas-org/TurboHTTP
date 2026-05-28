using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H2;

[Collection("H2")]
public sealed class SmokeSpec : IntegrationSpecBase
{
    public SmokeSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H2, tls: true);

    [Fact(Timeout = 30000)]
    public async Task Get_should_return_200()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Post_should_echo_request_body()
    {
        const string payload = """{"test":"h2"}""";
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
    public async Task Concurrent_requests_should_succeed()
    {
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"),
                CancellationToken));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Status_endpoint_should_return_requested_status_code()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/status/204"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Large_response_should_be_received_completely()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/bytes/32768"),
            CancellationToken);

        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(32768, content.Length);
    }
}