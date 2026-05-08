using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Protocol;

public sealed class ResilienceSpec : FeatureSpecBase
{
    public ResilienceSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Resilience_should_succeed_with_slow_headers_within_timeout(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/resilience/slow-headers/200"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
