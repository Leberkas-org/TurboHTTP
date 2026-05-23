using System.Net;
using System.Net.Http.Headers;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.Features;

public sealed class RangeFeatureSpec : FeatureSpecBase
{
    public RangeFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Range_should_return_full_content_without_header(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/range/1024"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(1024, content.Length);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Range_should_return_partial_content(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);

        var request = new HttpRequestMessage(HttpMethod.Get, "/range/1024");
        request.Headers.Range = new RangeHeaderValue(0, 511);

        var response = await helper.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(512, content.Length);
        Assert.NotNull(response.Content.Headers.ContentRange);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Range_should_return_suffix_range(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);

        var request = new HttpRequestMessage(HttpMethod.Get, "/range/1024");
        request.Headers.Range = new RangeHeaderValue(null, 256);

        var response = await helper.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(256, content.Length);
    }
}
