using System.Net;
using System.Text.Json;
using TurboHTTP.Client;
using TurboHTTP.Tests.Shared;
using TurboHTTP.IntegrationTests.Client.Shared;

namespace TurboHTTP.IntegrationTests.Features;

public sealed class CompressionFeatureSpec : FeatureSpecBase
{
    public CompressionFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Decompression_should_transparently_decompress_gzip(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithDecompression());

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("gzipped").GetBoolean());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Decompression_should_transparently_decompress_deflate(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithDecompression());

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/deflate"), CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("deflated").GetBoolean());
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Decompression_should_handle_uncompressed_response(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithDecompression());

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Decompression_should_decompress_sequentially(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.WithDecompression());

        var r1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/gzip"), CancellationToken);
        var b1 = await r1.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(JsonDocument.Parse(b1).RootElement.GetProperty("gzipped").GetBoolean());

        var r2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/deflate"), CancellationToken);
        var b2 = await r2.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(JsonDocument.Parse(b2).RootElement.GetProperty("deflated").GetBoolean());
    }
}