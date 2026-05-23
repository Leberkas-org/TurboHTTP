using System.Net;
using TurboHTTP.Tests.Shared;
using TurboHTTP.IntegrationTests.Client.Shared;

namespace TurboHTTP.IntegrationTests.Features;

public sealed class StreamingFeatureSpec : FeatureSpecBase
{
    public StreamingFeatureSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task StreamBytes_should_return_exact_byte_count(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/stream-bytes/4096"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(4096, content.Length);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task StreamBytes_should_handle_large_payload(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/stream-bytes/65536"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(64 * 1024, content.Length);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Drip_should_deliver_bytes_over_duration(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/drip?numbytes=5&duration=2"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        sw.Stop();

        Assert.Equal(5, content.Length);
        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(1),
            $"Expected at least 1s elapsed, got {sw.Elapsed}");
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(AllVariants))]
    public async Task Drip_should_abort_on_timeout(ProtocolVariant variant)
    {
        await using var helper = CreateClient(variant);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/drip?numbytes=50&duration=8&delay=0"), cts.Token);
            await response.Content.ReadAsByteArrayAsync(cts.Token).WaitAsync(cts.Token);
        });

        Assert.True(
            ex is OperationCanceledException or HttpRequestException or TaskCanceledException,
            $"Expected OperationCanceledException, TaskCanceledException or HttpRequestException, got {ex.GetType().Name}");
    }
}
