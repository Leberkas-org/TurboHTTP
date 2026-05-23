using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H10;

[Collection("H10")]
public sealed class TransferSpec : IntegrationSpecBase
{
    public TransferSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H10, Tls: false);

    [Theory(Timeout = 15000)]
    [InlineData(128)]
    [InlineData(1024)]
    [InlineData(8192)]
    [InlineData(65536)]
    public async Task Transfer_should_receive_binary_body_of_exact_size(int size)
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/bytes/{size}"), CancellationToken);

        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(size, content.Length);
    }

    [Fact(Timeout = 30000)]
    public async Task Transfer_should_receive_large_100kb_body()
    {
        const int size = 100 * 1024;
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/bytes/{size}"), CancellationToken);

        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(size, content.Length);
    }

    [Fact(Timeout = 15000)]
    public async Task Transfer_should_handle_empty_body_for_204()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/status/204"), CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [InlineData(200, HttpStatusCode.OK)]
    [InlineData(201, HttpStatusCode.Created)]
    [InlineData(204, HttpStatusCode.NoContent)]
    [InlineData(301, HttpStatusCode.MovedPermanently)]
    [InlineData(400, HttpStatusCode.BadRequest)]
    [InlineData(401, HttpStatusCode.Unauthorized)]
    [InlineData(403, HttpStatusCode.Forbidden)]
    [InlineData(404, HttpStatusCode.NotFound)]
    [InlineData(405, HttpStatusCode.MethodNotAllowed)]
    [InlineData(418, (HttpStatusCode)418)]
    [InlineData(500, HttpStatusCode.InternalServerError)]
    [InlineData(502, HttpStatusCode.BadGateway)]
    [InlineData(503, HttpStatusCode.ServiceUnavailable)]
    public async Task Transfer_should_return_correct_status_code(int code, HttpStatusCode expected)
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/status/{code}"), CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Transfer_should_echo_large_post_body()
    {
        var payload = new string('X', 2048);
        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };
        request.Headers.ConnectionClose = true;

        var response = await Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains(payload, body);
    }

    [Fact(Timeout = 15000)]
    public async Task Transfer_should_echo_binary_post_body()
    {
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);

        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}