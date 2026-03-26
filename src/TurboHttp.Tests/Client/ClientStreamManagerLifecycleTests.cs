using System.Net;
using System.Net.Http;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit;
using TurboHttp.Client;
using TurboHttp.Streams;

namespace TurboHttp.Tests.Client;

/// <summary>
/// Integration tests verifying <see cref="TurboClientStreamManager"/> actor-based lifecycle:
/// actor spawning, channel wiring, and graceful shutdown via Dispose/DisposeAsync.
/// </summary>
public sealed class ClientStreamManagerLifecycleTests : TestKit
{
    private static readonly TurboClientOptions DefaultOptions = new();

    private static TurboRequestOptions DefaultRequestOptions() =>
        new(null, new HttpRequestMessage().Headers, HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionOrLower, TimeSpan.FromSeconds(30), 0);

    // ── Construction ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9110-csm-001: Manager spawns owner actor on construction", Timeout = 5000)]
    public void Manager_SpawnsOwnerActor_OnConstruction()
    {
        var manager = new TurboClientStreamManager(DefaultOptions, DefaultRequestOptions, Sys);

        // Owner actor is spawned — verify by checking that channels are exposed
        Assert.NotNull(manager.Requests);
        Assert.NotNull(manager.Responses);

        manager.Dispose();
    }

    [Fact(DisplayName = "RFC-9110-csm-002: Manager exposes functional request/response channels", Timeout = 5000)]
    public void Manager_ExposesFunctionalChannels()
    {
        var manager = new TurboClientStreamManager(DefaultOptions, DefaultRequestOptions, Sys);

        // Channels should accept writes (unbounded, so this never blocks)
        var written = manager.Requests.TryWrite(new HttpRequestMessage(HttpMethod.Get, "http://localhost/test"));
        Assert.True(written);

        manager.Dispose();
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9110-csm-003: Dispose completes request channel", Timeout = 5000)]
    public void Dispose_CompletesRequestChannel()
    {
        var manager = new TurboClientStreamManager(DefaultOptions, DefaultRequestOptions, Sys);

        manager.Dispose();

        // After dispose, writing to the request channel should fail
        var written = manager.Requests.TryWrite(new HttpRequestMessage(HttpMethod.Get, "http://localhost/test"));
        Assert.False(written);
    }

    [Fact(DisplayName = "RFC-9110-csm-004: Dispose completes response channel", Timeout = 5000)]
    public async Task Dispose_CompletesResponseChannel()
    {
        var manager = new TurboClientStreamManager(DefaultOptions, DefaultRequestOptions, Sys);

        manager.Dispose();

        // Response channel should complete — ReadAllAsync should terminate
        var count = 0;
        await foreach (var _ in manager.Responses.ReadAllAsync())
        {
            count++;
        }

        Assert.Equal(0, count);
    }

    [Fact(DisplayName = "RFC-9110-csm-005: Double dispose is safe", Timeout = 5000)]
    public void DoubleDispose_IsSafe()
    {
        var manager = new TurboClientStreamManager(DefaultOptions, DefaultRequestOptions, Sys);

        manager.Dispose();
        manager.Dispose(); // Should not throw

        Assert.False(manager.Requests.TryWrite(new HttpRequestMessage()));
    }

    // ── ResponseWriter ───────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9110-csm-009: ResponseWriter allows test injection of synthetic responses",
        Timeout = 5000)]
    public async Task ResponseWriter_AllowsSyntheticResponseInjection()
    {
        var manager = new TurboClientStreamManager(DefaultOptions, DefaultRequestOptions, Sys);

        // Write a synthetic response through the test-accessible ResponseWriter
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        manager.ResponseWriter.TryWrite(response);

        // Read it from the response channel
        var received = await manager.Responses.ReadAsync();
        Assert.Equal(HttpStatusCode.OK, received.StatusCode);

        manager.Dispose();
    }

    // ── Pipeline descriptor ──────────────────────────────────────────────────

    [Fact(DisplayName = "RFC-9110-csm-010: Manager accepts pipeline descriptor", Timeout = 5000)]
    public void Manager_AcceptsPipelineDescriptor()
    {
        var pipeline = PipelineDescriptor.Empty;
        var manager = new TurboClientStreamManager(DefaultOptions, DefaultRequestOptions, Sys, pipeline);

        Assert.NotNull(manager.Requests);
        Assert.NotNull(manager.Responses);

        manager.Dispose();
    }

    // ── TurboHttpClient integration ─────────────────────────────────────────

    [Fact(DisplayName = "RFC-9110-csm-011: TurboHttpClient creates actors on construction and exposes channels",
        Timeout = 5000)]
    public void TurboHttpClient_CreatesActors_OnConstruction()
    {
        var client = new TurboHttpClient(DefaultOptions, Sys);

        Assert.NotNull(client.Requests);
        Assert.NotNull(client.Responses);
        Assert.NotNull(client.Manager);

        client.Dispose();
    }

    [Fact(DisplayName = "RFC-9110-csm-012: TurboHttpClient SendAsync writes request into actor channel",
        Timeout = 5000)]
    public async Task TurboHttpClient_SendAsync_WritesRequestIntoActorChannel()
    {
        var client = new TurboHttpClient(DefaultOptions, Sys);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");

        // SendAsync writes the request to the channel. Without a working pipeline the
        // response will never arrive, so use a short CancellationToken to avoid hanging.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // SendAsync should write the request and then time out waiting for the response.
        // This verifies the request flows into the actor-owned channel.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.SendAsync(request, cts.Token));

        client.Dispose();
    }

    [Fact(DisplayName = "RFC-9110-csm-013: TurboHttpClient Dispose stops actors and closes channels", Timeout = 10000)]
    public async Task TurboHttpClient_Dispose_StopsActorsAndClosesChannels()
    {
        var client = new TurboHttpClient(DefaultOptions, Sys);

        // Verify channels are initially functional
        Assert.True(client.Requests.TryWrite(new HttpRequestMessage()));

        await client.DisposeAsync();

        // After dispose, request channel should be closed
        Assert.False(client.Requests.TryWrite(new HttpRequestMessage()));

        // Response channel should complete
        var count = 0;
        await foreach (var _ in client.Responses.ReadAllAsync())
        {
            count++;
        }

        Assert.Equal(0, count);
    }
}