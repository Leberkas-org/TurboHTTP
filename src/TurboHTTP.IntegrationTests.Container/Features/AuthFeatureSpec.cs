using System.Net;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.Features;

public sealed class AuthFeatureSpec : FeatureSpecBase
{
    public AuthFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Auth_should_succeed_with_correct_credentials(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, configureOptions: opts =>
        {
            opts.Credentials = new NetworkCredential("testuser", "testpass");
            opts.PreAuthenticate = true;
        });
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/basic-auth/testuser/testpass"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Auth_should_return_401_without_credentials(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/basic-auth/testuser/testpass"), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Auth_should_return_401_with_wrong_credentials(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, configureOptions: opts =>
        {
            opts.Credentials = new NetworkCredential("wrong", "wrong");
            opts.PreAuthenticate = true;
        });
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/basic-auth/testuser/testpass"), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Auth_should_not_send_header_when_preauthenticate_disabled(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol, configureOptions: opts =>
        {
            opts.Credentials = new NetworkCredential("testuser", "testpass");
            opts.PreAuthenticate = false;
        });
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/basic-auth/testuser/testpass"), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
