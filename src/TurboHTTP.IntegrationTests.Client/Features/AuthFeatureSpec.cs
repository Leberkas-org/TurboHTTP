using System.Net;
using System.Net.Http.Headers;
using TurboHTTP.Client;
using TurboHTTP.Tests.Shared;
using TurboHTTP.IntegrationTests.Client.Shared;

namespace TurboHTTP.IntegrationTests.Features;

public sealed class AuthFeatureSpec : FeatureSpecBase
{
    public AuthFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Auth_should_succeed_with_correct_credentials(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, configureOptions: opts =>
        {
            opts.Credentials = new NetworkCredential("testuser", "testpass");
            opts.PreAuthenticate = true;
        });

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/basic-auth/testuser/testpass"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Auth_should_return_401_without_credentials(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/basic-auth/testuser/testpass"), CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Auth_should_return_401_with_wrong_credentials(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, configureOptions: opts =>
        {
            opts.Credentials = new NetworkCredential("wrong", "wrong");
            opts.PreAuthenticate = true;
        });

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/basic-auth/testuser/testpass"), CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Auth_should_not_send_header_when_preauthenticate_disabled(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, configureOptions: opts =>
        {
            opts.Credentials = new NetworkCredential("testuser", "testpass");
            opts.PreAuthenticate = false;
        });

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/basic-auth/testuser/testpass"), CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Auth_should_succeed_with_bearer_token(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant, b => b.UseRequest(req =>
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
            return req;
        }));

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/bearer"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(AllVariants))]
    public async Task Auth_should_return_401_without_bearer_token(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/bearer"), CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}