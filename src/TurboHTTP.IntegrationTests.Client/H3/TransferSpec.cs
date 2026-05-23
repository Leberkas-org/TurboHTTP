using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.Tests.Shared;
using TurboHTTP.IntegrationTests.Client.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
public sealed class TransferSpec : IntegrationSpecBase
{
    public TransferSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H3, Tls: true);

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
    }
}