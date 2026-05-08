using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Features;

public sealed class CompressionFeatureSpec : FeatureSpecBase
{
    public CompressionFeatureSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Decompression_should_transparently_decompress_gzip(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithDecompression());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/compress/gzip/1"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(ct);
        Assert.Equal(1024, content.Length);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Decompression_should_transparently_decompress_deflate(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithDecompression());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/compress/deflate/1"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(ct);
        Assert.Equal(1024, content.Length);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Decompression_should_transparently_decompress_brotli(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithDecompression());
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/compress/br/1"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(ct);
        Assert.Equal(1024, content.Length);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Decompression_should_negotiate_accept_encoding(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithDecompression());
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/negotiate");
        request.Headers.Add("Accept-Encoding", "gzip, deflate, br");

        var response = await helper.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.False(string.IsNullOrEmpty(body));
    }
}
