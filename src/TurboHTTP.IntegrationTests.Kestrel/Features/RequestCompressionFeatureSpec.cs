using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Features;

public sealed class RequestCompressionFeatureSpec : FeatureSpecBase
{
    public RequestCompressionFeatureSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task RequestCompression_should_compress_with_gzip(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRequestCompression(c => c.Encoding = "gzip"));
        var ct = TestContext.Current.CancellationToken;

        var payload = new string('A', 2048);
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/compress/verify-gzip")
            {
                Content = new StringContent(payload)
            }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Equal(payload, body);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task RequestCompression_should_compress_with_deflate(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRequestCompression(c => c.Encoding = "deflate"));
        var ct = TestContext.Current.CancellationToken;

        var payload = new string('B', 2048);
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/compress/verify-deflate")
            {
                Content = new StringContent(payload)
            }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Equal(payload, body);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task RequestCompression_should_compress_with_brotli(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRequestCompression(c => c.Encoding = "br"));
        var ct = TestContext.Current.CancellationToken;

        var payload = new string('C', 2048);
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/compress/verify-br")
            {
                Content = new StringContent(payload)
            }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Equal(payload, body);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task RequestCompression_should_not_compress_small_body(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, b => b.WithRequestCompression());
        var ct = TestContext.Current.CancellationToken;

        var payload = "small";
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/compress/echo")
            {
                Content = new StringContent(payload)
            }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        if (response.Headers.TryGetValues("X-Content-Encoding", out var values))
        {
            Assert.DoesNotContain("gzip", values);
        }
    }
}
