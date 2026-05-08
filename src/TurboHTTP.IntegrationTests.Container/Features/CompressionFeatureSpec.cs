using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.Features;

public sealed class CompressionFeatureSpec : FeatureSpecBase
{
    public CompressionFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Decompression_should_transparently_decompress_gzip(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithDecompression());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("gzipped").GetBoolean());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Decompression_should_transparently_decompress_deflate(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithDecompression());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/deflate"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("deflated").GetBoolean());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Decompression_should_handle_uncompressed_response(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithDecompression());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Decompression_should_decompress_sequentially(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithDecompression());
        var ct = TestContext.Current.CancellationToken;

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), ct);
        var b1 = await r1.Content.ReadAsStringAsync(ct);
        Assert.True(JsonDocument.Parse(b1).RootElement.GetProperty("gzipped").GetBoolean());

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/deflate"), ct);
        var b2 = await r2.Content.ReadAsStringAsync(ct);
        Assert.True(JsonDocument.Parse(b2).RootElement.GetProperty("deflated").GetBoolean());
    }
}
