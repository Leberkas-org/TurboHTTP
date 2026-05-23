using System.Net;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.Features;

public sealed class TimingFeatureSpec : FeatureSpecBase
{
    public TimingFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Delay_should_return_200_when_within_timeout(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        helper.Client.Timeout = TimeSpan.FromSeconds(15);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/delay/1"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Delay_should_throw_when_timeout_exceeded(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        helper.Client.Timeout = TimeSpan.FromSeconds(1);

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/delay/5"), CancellationToken);
        });

        Assert.True(
            ex is OperationCanceledException or HttpRequestException,
            $"Expected OperationCanceledException or HttpRequestException, got {ex.GetType().Name}");
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Delay_should_abort_on_cancellation_token(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        helper.Client.Timeout = TimeSpan.FromSeconds(30);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/delay/10"), cts.Token);
        });
    }
}
