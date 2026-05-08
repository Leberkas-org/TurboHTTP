using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Features;

public sealed class AuthFeatureSpec : FeatureSpecBase
{
    public AuthFeatureSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Auth_should_inject_authorization_header(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, configureOptions: opts =>
        {
            opts.Credentials = new NetworkCredential("testuser", "testpass");
            opts.PreAuthenticate = true;
        });
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/auth"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Auth_should_return_401_without_credentials(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/auth"), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
