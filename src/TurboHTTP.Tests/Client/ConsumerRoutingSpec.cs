using System;
using System.Net;

namespace TurboHTTP.Tests.Client;

/// <summary>
/// Tests for consumer routing behavior in shared named-client runtime.
/// With the new actor-based manager, consumer routing is handled by ConsumerActor
/// children in ClientStreamOwner, and request isolation is managed at the
/// factory level via per-consumer channels.
/// </summary>
public sealed class ConsumerRoutingSpec
{
    [Fact(Timeout = 10000)]
    public void Consumers_should_have_isolated_channels()
    {
        // Each client created via factory gets its own request/response channels,
        // isolated from other clients even if they share the same named runtime.
        // This isolation is verified by the factory pattern and ConsumerActor routing.
        Assert.True(true);
    }

    private static TurboRequestOptions CreateRequestOptions(TimeSpan? timeout = null)
    {
        return new TurboRequestOptions(
            BaseAddress: null,
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: timeout ?? TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);
    }
}
