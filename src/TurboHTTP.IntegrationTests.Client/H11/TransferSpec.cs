using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.Tests.Shared;
using TurboHTTP.IntegrationTests.Client.Shared;

namespace TurboHTTP.IntegrationTests.H11;

[Collection("H11")]
public sealed class TransferSpec : IntegrationSpecBase
{
    public TransferSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H11, Tls: false);

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
        var payload = new string('X', 8192);
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

    [Fact(Timeout = 15000)]
    public async Task Transfer_should_receive_streaming_response()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/stream/5"), CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, lines.Length);

        foreach (var line in lines)
        {
            var json = JsonDocument.Parse(line);
            Assert.True(json.RootElement.TryGetProperty("id", out _));
        }
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

    [Fact(Timeout = 30000)]
    public async Task Transfer_should_handle_sequential_large_bodies()
    {
        for (var i = 0; i < 3; i++)
        {
            var response = await Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/bytes/32768"), CancellationToken);

            var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(32768, content.Length);
        }
    }

    [Theory(Timeout = 30000)]
    [InlineData(1024)]
    [InlineData(65536)]
    [InlineData(102400)]
    public async Task Transfer_should_receive_large_body_over_tls(int size)
    {
        await using var helper = CreateClient(new ProtocolVariant(TestHttpVersion.H11, Tls: true));
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/bytes/{size}"), CancellationToken);

        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(size, content.Length);
    }

    [Fact(Timeout = 15000)]
    public async Task Transfer_should_receive_streaming_response_over_tls()
    {
        await using var helper = CreateClient(new ProtocolVariant(TestHttpVersion.H11, Tls: true));
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/stream/3"), CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, lines.Length);
    }
}