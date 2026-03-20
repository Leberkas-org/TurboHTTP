using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TurboHttp.Client;
using TurboHttp.IO;

namespace TurboHttp.Tests.Client;

/// <summary>
/// Verifies that <see cref="TurboHttpClient"/> always removes the pending
/// <see cref="TaskCompletionSource{T}"/> from its internal dictionary after
/// completion, timeout, or cancellation.
/// </summary>
public sealed class TurboHttpClientPendingTests
{
    private static ConcurrentDictionary<Guid, TaskCompletionSource<HttpResponseMessage>> GetPending(
        TurboHttpClient client)
    {
        var field = typeof(TurboHttpClient)
            .GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (ConcurrentDictionary<Guid, TaskCompletionSource<HttpResponseMessage>>)field.GetValue(client)!;
    }

    private static TurboClientStreamManager GetManager(TurboHttpClient client)
    {
        var field = typeof(TurboHttpClient)
            .GetField("_manager", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (TurboClientStreamManager)field.GetValue(client)!;
    }

    private static HttpRequestOptionsKey<Guid> GetKey(TurboHttpClient client)
    {
        var field = typeof(TurboHttpClient)
            .GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (HttpRequestOptionsKey<Guid>)field.GetValue(client)!;
    }

    private static ActorSystem CreateSystem(string name)
    {
        var diSetup = DependencyResolverSetup.Create(new ServiceCollection().BuildServiceProvider());
        var system = ActorSystem.Create(name, BootstrapSetup.Create().And(diSetup));
        var clientManager = system.ActorOf(Props.Create(() => new ClientManager()), "client-manager");
        ActorRegistry.For(system).Register<ClientManager>(clientManager);
        return system;
    }

    [Fact(DisplayName = "RFC-pending-001: _pending is empty after timeout")]
    public async Task Should_EmptyPending_After_Timeout()
    {
        var system = CreateSystem("test-pending-timeout");
        try
        {
            var client = new TurboHttpClient(new TurboClientOptions(), system)
            {
                Timeout = TimeSpan.FromMilliseconds(50)
            };
            var pending = GetPending(client);

            // 127.0.0.1:1 is typically closed — connection will never succeed
            var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1/");
            await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync(request, CancellationToken.None));

            Assert.Empty(pending);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact(DisplayName = "RFC-pending-002: _pending is empty after cancellation")]
    public async Task Should_EmptyPending_After_Cancellation()
    {
        var system = CreateSystem("test-pending-cancel");
        try
        {
            var client = new TurboHttpClient(new TurboClientOptions(), system)
            {
                Timeout = TimeSpan.FromSeconds(30) // long timeout — we cancel manually
            };
            var pending = GetPending(client);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1/");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(request, cts.Token));

            Assert.Empty(pending);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact(DisplayName = "RFC-pending-004: pending requests fail when response channel closes with error")]
    public async Task Should_FailPendingRequests_When_ResponseChannelClosesWithError()
    {
        var system = CreateSystem("test-pending-channel-error");
        try
        {
            var client = new TurboHttpClient(new TurboClientOptions(), system)
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            var pending = GetPending(client);
            var manager = GetManager(client);
            var key = GetKey(client);

            // Register two pending TCS entries as SendAsync would
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var tcs1 = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions
                .RunContinuationsAsynchronously);
            var tcs2 = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions
                .RunContinuationsAsynchronously);
            pending.TryAdd(id1, tcs1);
            pending.TryAdd(id2, tcs2);

            // Close the response channel with an error to simulate stream failure
            var channelError = new InvalidOperationException("simulated stream failure");
            manager.ResponseWriter.Complete(channelError);

            // Both pending TCS instances must be faulted with the channel error
            await Assert.ThrowsAsync<InvalidOperationException>(() => tcs1.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => tcs2.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            // Give DrainResponsesAsync a moment to clear the dictionary
            await Task.Delay(10);
            Assert.Empty(pending);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact(DisplayName = "RFC-pending-003: _pending is empty after success")]
    public async Task Should_EmptyPending_After_Success()
    {
        var system = CreateSystem("test-pending-success");
        try
        {
            var client = new TurboHttpClient(new TurboClientOptions(), system)
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            var pending = GetPending(client);
            var manager = GetManager(client);
            var key = GetKey(client);

            // Build a fake request with the internal RequestId option pre-set
            var requestId = Guid.NewGuid();
            var fakeRequest = new HttpRequestMessage(HttpMethod.Get, "http://fake/");
            fakeRequest.Options.Set(key, requestId);

            // Register a TCS in _pending as SendAsync would
            var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.TryAdd(requestId, tcs);

            // Inject a fake response directly into the response channel so DrainResponsesAsync picks it up
            var fakeResponse = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = fakeRequest };
            await manager.ResponseWriter.WriteAsync(fakeResponse);

            // Wait for DrainResponsesAsync to complete the TCS
            var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Give DrainResponsesAsync a moment to remove the entry then check
            await Task.Delay(10);
            Assert.Empty(pending);
        }
        finally
        {
            await system.Terminate();
        }
    }
}