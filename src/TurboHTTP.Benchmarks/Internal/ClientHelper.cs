using TurboHTTP.Client;
using Akka.Actor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace TurboHTTP.Benchmarks.Internal;

/// <summary>
/// Lightweight factory helper that creates <see cref="ITurboHttpClient"/> instances
/// for benchmark use. Wraps the DI setup required by <see cref="TurboHttpClientFactory"/>.
/// Each instance owns its own <see cref="ActorSystem"/> and terminates it on disposal,
/// preventing stale PinnedDispatcher threads from accumulating across BDN parameter combinations.
/// </summary>
internal sealed class ClientHelper : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ActorSystem _system;

    private ClientHelper(ServiceProvider provider, ITurboHttpClient client, ActorSystem system)
    {
        _provider = provider;
        Client = client;
        _system = system;
    }

    /// <summary>The configured <see cref="ITurboHttpClient"/> instance.</summary>
    public ITurboHttpClient Client { get; }

    /// <summary>
    /// Creates a new <see cref="ClientHelper"/> with a fully configured TurboHttp client
    /// targeting a remote URI (e.g. <c>https://binkraken.com</c>) for SendAsync benchmarks.
    /// </summary>
    /// <param name="baseAddress">The remote base URI (scheme + host).</param>
    /// <param name="version">The HTTP version to use.</param>
    public static ClientHelper CreateClient(Uri baseAddress, Version version)
    {
        var options = new TurboClientOptions
        {
            BaseAddress = baseAddress,
            DangerousAcceptAnyServerCertificate = true,
            // H1.x: many connections with shallow pipelining to handle CL up to 8192.
            Http1 = new Http1Options
            {
                MaxConnectionsPerServer = 512,
                MaxPipelineDepth = 2
            },
            // H2: 16 connections × 1000 streams = 16 000 in-flight capacity.
            Http2 = new Http2Options
            {
                MaxConnectionsPerServer = 16,
                MaxConcurrentStreams = 1000
            },
            // H3: 8 connections × 1000 streams = 8000 in-flight capacity.
            // QPACK dynamic table at 32 KiB for better header compression on repeated requests.
            Http3 = new Http3Options
            {
                MaxConnectionsPerServer = 8,
                MaxConcurrentStreams = 1000,
                QpackMaxTableCapacity = 32_768,
                QpackBlockedStreams = 200,
                MaxFieldSectionSize = 65_536,
                IdleTimeout = TimeSpan.FromMinutes(5),
                MaxReconnectAttempts = 10,
                MaxReconnectBufferSize = 256,
            },
        };

        return Build(baseAddress, version, options);
    }

    /// <summary>
    /// Creates a new <see cref="ClientHelper"/> with streaming-optimised options
    /// targeting a remote URI for channel-based benchmarks.
    /// </summary>
    /// <param name="baseAddress">The remote base URI (scheme + host).</param>
    /// <param name="version">The HTTP version to use.</param>
    public static ClientHelper CreateStreamingClient(Uri baseAddress, Version version)
    {
        var options = new TurboClientOptions
        {
            BaseAddress = baseAddress,
            DangerousAcceptAnyServerCertificate = true,
            // Streaming: fewer connections but deep pipelining via the channel.
            Http1 = new Http1Options { MaxConnectionsPerServer = 4, MaxPipelineDepth = 2048 },
            // H2: 16 connections × 1000 streams for high-CL streaming.
            Http2 = new Http2Options { MaxConnectionsPerServer = 16, MaxConcurrentStreams = 1000 },
            // H3: 8 connections × 1000 streams, larger QPACK table for repeated header patterns.
            Http3 = new Http3Options
            {
                MaxConnectionsPerServer = 8,
                MaxConcurrentStreams = 1000,
                QpackMaxTableCapacity = 32_768,
                QpackBlockedStreams = 200,
                MaxFieldSectionSize = 65_536,
                IdleTimeout = TimeSpan.FromMinutes(5),
                MaxReconnectAttempts = 10,
                MaxReconnectBufferSize = 256,
            },
            MaxEndpointSubstreams = 16384,
        };

        return Build(baseAddress, version, options);
    }

    private static ClientHelper Build(Uri baseAddress, Version version, TurboClientOptions options)
    {
        var services = new ServiceCollection();

        // Create and register the ActorSystem explicitly so it can be terminated on disposal.
        // Without this, TurboHttpClientFactory creates an untracked ActorSystem that is never
        // terminated, causing PinnedDispatcher threads to accumulate across BDN combinations.
        var system = ActorSystem.Create($"turbohttp-bench-{Guid.NewGuid():N}");
        services.AddSingleton(system);

        services.AddTurboHttpClient();
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<TurboClientOptions>>(
            new FixedOptionsFactory(options)));

        var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<ITurboHttpClientFactory>();
        var client = factory.CreateClient(string.Empty);
        client.BaseAddress = baseAddress;
        client.DefaultRequestVersion = version;
        client.Timeout = TimeSpan.FromMinutes(5);

        return new ClientHelper(provider, client, system);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Signal pipeline to drain
        Client.Requests.TryComplete();

        try
        {
            await Client.Responses.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Pipeline may complete with an error during shutdown — that is fine.
        }

        Client.Dispose();

        // Terminate the ActorSystem to stop all PinnedDispatcher threads.
        // Without this, each BDN parameter combination leaks ~50–100 OS threads, causing
        // scheduling contention that inflates latency 13× by combination #13 (CL=64).
        await _system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        await _system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5));

        // Allow dispatcher threads to fully wind down before the next combination starts.
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        await _provider.DisposeAsync();
    }

    private sealed class FixedOptionsFactory(TurboClientOptions options) : IOptionsFactory<TurboClientOptions>
    {
        public TurboClientOptions Create(string name) => options;
    }
}