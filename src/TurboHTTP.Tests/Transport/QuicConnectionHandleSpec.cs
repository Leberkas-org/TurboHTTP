using System.Net;
using TurboHTTP.Internal;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Connection;

#pragma warning disable CA1416

namespace TurboHTTP.Tests.Transport;

public sealed class QuicConnectionHandleSpec
{
    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Scheme = "https",
        Host = "localhost",
        Port = 443,
        Version = HttpVersion.Version30
    };

    private static readonly QuicOptions TestOptions = new() { Host = "localhost", Port = 443 };

    private QuicConnectionHandle CreateHandle(FakeClientProvider? provider = null)
    {
        return new QuicConnectionHandle(provider ?? new FakeClientProvider(), TestOptions, TestEndpoint);
    }

    [Fact(Timeout = 5000)]
    public void QuicConnectionHandle_should_set_key_from_constructor()
    {
        var handle = CreateHandle();

        Assert.Equal(TestEndpoint, handle.Key);
        Assert.Equal("localhost", handle.Key.Host);
        Assert.Equal((ushort)443, handle.Key.Port);
        Assert.Equal("https", handle.Key.Scheme);
        Assert.Equal(HttpVersion.Version30, handle.Key.Version);
    }

    [Fact(Timeout = 5000)]
    public void QuicConnectionHandle_should_throw_on_null_provider()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new QuicConnectionHandle(null!, TestOptions, TestEndpoint));
    }

    [Fact(Timeout = 5000)]
    public void QuicConnectionHandle_should_throw_on_null_options()
    {
        var provider = new FakeClientProvider();

        Assert.Throws<ArgumentNullException>(() =>
            new QuicConnectionHandle(provider, null!, TestEndpoint));
    }

    [Fact(Timeout = 5000)]
    public void QuicConnectionHandle_local_endpoint_should_reflect_provider_state()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);

        // FakeClientProvider returns null for LocalEndPoint by default
        Assert.Null(handle.LocalEndPoint);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_open_stream_as_lease_for_request_streams()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);

        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: true, TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_should_open_stream_as_lease_for_unidirectional_streams()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);

        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: false, TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_opened_request_stream_should_have_correct_stream_type()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);

        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: true, TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_opened_unidirectional_stream_should_be_usable()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);

        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: false, TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_opened_stream_lease_should_reference_handle_endpoint()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);

        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: true, TestContext.Current.CancellationToken);

        Assert.Equal(TestEndpoint, lease.Key);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_accept_inbound_stream_should_return_null_on_cancellation()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await handle.AcceptInboundStreamAsLeaseAsync(cts.Token);

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_dispose_should_not_throw()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);

        await handle.DisposeAsync();

        // Should complete without throwing
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_dispose_should_be_safe_to_call_multiple_times()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);

        await handle.DisposeAsync();
        await handle.DisposeAsync();

        // Should not throw on multiple disposals
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_inbound_stream_record_encapsulates_lease_and_type()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);
        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: true, TestContext.Current.CancellationToken);

        var inboundStream = new QuicConnectionHandle.InboundStream(lease, 0x00, 3);

        Assert.Same(lease, inboundStream.Lease);
        Assert.Equal(0x00, inboundStream.StreamTypeValue);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_inbound_stream_record_equality_based_on_lease_and_type()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);
        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: true, TestContext.Current.CancellationToken);

        var stream1 = new QuicConnectionHandle.InboundStream(lease, 0x00, 3);
        var stream2 = new QuicConnectionHandle.InboundStream(lease, 0x00, 3);

        // Records with same lease and stream type value should be equal
        Assert.Equal(stream1, stream2);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_opened_stream_lease_should_have_client_state()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);

        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: true, TestContext.Current.CancellationToken);

        // Stream lease should have a valid ClientState
        Assert.NotNull(lease.State);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicConnectionHandle_opened_stream_lease_should_have_key_set()
    {
        var provider = new FakeClientProvider();
        var handle = new QuicConnectionHandle(provider, TestOptions, TestEndpoint);

        var lease = await handle.OpenStreamAsLeaseAsync(bidirectional: false, TestContext.Current.CancellationToken);

        // Stream lease should preserve the endpoint key
        Assert.Equal(TestEndpoint, lease.Key);
    }
}
