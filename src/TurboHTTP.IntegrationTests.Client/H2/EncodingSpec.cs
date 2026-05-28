using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H2;

[Collection("H2")]
public sealed class EncodingSpec : IntegrationSpecBase
{
    public EncodingSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H2, tls: true);

    [Fact(Timeout = 15000)]
    public async Task Encoding_should_decompress_gzip_response()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("gzipped").GetBoolean());
    }

    [Fact(Timeout = 15000)]
    public async Task Encoding_should_decompress_deflate_response()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/deflate"), CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("deflated").GetBoolean());
    }

    [Fact(Timeout = 15000)]
    public async Task Encoding_should_negotiate_accept_encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/get");
        request.Headers.Add("Accept-Encoding", "gzip, deflate");

        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.False(string.IsNullOrEmpty(body));
    }

    [Fact(Timeout = 15000)]
    public async Task Encoding_should_decompress_sequentially_on_same_connection()
    {
        var r1 = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), CancellationToken);
        var b1 = await r1.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(JsonDocument.Parse(b1).RootElement.GetProperty("gzipped").GetBoolean());

        var r2 = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/deflate"), CancellationToken);
        var b2 = await r2.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(JsonDocument.Parse(b2).RootElement.GetProperty("deflated").GetBoolean());

        var r3 = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), CancellationToken);
        var b3 = await r3.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(JsonDocument.Parse(b3).RootElement.GetProperty("gzipped").GetBoolean());
    }
}