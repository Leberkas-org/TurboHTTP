using Akka.Actor;
using Akka.Configuration;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TurboHTTP.Benchmarks.Internal;
using TurboHTTP.Internal;

namespace TurboHTTP.Benchmarks;

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
    /// Creates a new <see cref="ClientHelper"/> with a fully configured TurboHttp client.
    /// </summary>
    /// <param name="port">The port the test server is listening on.</param>
    /// <param name="version">The HTTP version to use (e.g. <c>new Version(1, 1)</c>).</param>
    public static ClientHelper CreateClient(int port, Version version)
    {
        var services = new ServiceCollection();

        var options = new TurboClientOptions
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            DangerousAcceptAnyServerCertificate = true,
            // H1.x: 512 connections × MaxPipelineDepth(2) = 1024 in-flight capacity.
            Http1 = new Http1Options { MaxConnectionsPerServer = 512, MaxPipelineDepth = 2 },
            // H2: 2 connections × 500 streams = 1000 in-flight capacity.
            // Server is configured with Http2.MaxStreamsPerConnection = 512; stay just
            // under that limit so we saturate each connection without triggering resets.
            Http2 = new Http2Options { MaxConnectionsPerServer = 2, MaxConcurrentStreams = 500 },
        };

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
        client.BaseAddress = options.BaseAddress;
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

/// <summary>
/// Comparative benchmarks measuring <see cref="ITurboHttpClient"/> performance for a
/// single sequential request across light (no body) and heavy (10 KB body) payloads.
/// Parameterized by HTTP version (1.1 and 2.0).
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(5)]
[InvocationCount(32)]
public class TurboHttpSingleRequestBenchmarks : BenchmarkBaseClass
{
    private ClientHelper _clientHelper = null!;
    private static readonly byte[] HeavyPayload = GeneratePayload(10 * 1024);

    /// <summary>
    /// Creates an <see cref="ITurboHttpClient"/> via <see cref="ClientHelper.CreateClient"/>
    /// configured for the current HTTP version, then warms it up with a single request.
    /// </summary>
    [GlobalSetup]
    public override void GlobalSetup()
    {
        base.GlobalSetup();
        _clientHelper = ClientHelper.CreateClient(KestrelPort, HttpVersionValue);
        WarmupRequest().GetAwaiter().GetResult();
    }

    /// <summary>Disposes the <see cref="ITurboHttpClient"/> and tears down the shared server.</summary>
    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        await _clientHelper.DisposeAsync();
        await base.GlobalCleanup();
    }

    /// <inheritdoc />
    public override async Task WarmupRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/benchmark/simple");
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Issues a single GET request to <c>/benchmark/simple</c> and discards the response.
    /// Measures per-request overhead for the minimal (no body) payload scenario.
    /// </summary>
    [Benchmark]
    public async Task SingleRequest_Light()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/benchmark/simple");
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Issues a single POST request with a 10 KB body to <c>/benchmark/payload</c>.
    /// Measures per-request overhead for the heavy payload scenario.
    /// </summary>
    [Benchmark]
    public async Task SingleRequest_Heavy()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/benchmark/payload")
        {
            Content = new ByteArrayContent(HeavyPayload)
        };
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Comparative benchmarks measuring <see cref="ITurboHttpClient"/> performance under concurrent
/// load. N requests are fired concurrently using <see cref="Task.WhenAll"/> and awaited as a unit.
/// Parameterized by <see cref="BenchmarkBaseClass.ConcurrencyLevel"/> and HTTP version.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(5)]
[InvocationCount(32)]
public class TurboHttpConcurrentBenchmarks : BenchmarkBaseClass
{
    private ClientHelper _clientHelper = null!;
    private static readonly byte[] HeavyPayload = GeneratePayload(10 * 1024);

    // Pre-allocated per parameter combination — avoids Task[] heap allocation inside the hot path.
    // Safe to reuse: all 32 invocations within one iteration are sequential; the array is
    // fully overwritten before Task.WhenAll reads it.
    private Task[] _tasks = null!;

    /// <summary>
    /// Creates an <see cref="ITurboHttpClient"/> via <see cref="ClientHelper.CreateClient"/>
    /// configured for the current HTTP version, then warms it up with a single request.
    /// The client is intentionally long-lived across all iterations: steady-state throughput
    /// (warm connections, pre-established streams) is what these benchmarks measure.
    /// </summary>
    [GlobalSetup]
    public override void GlobalSetup()
    {
        base.GlobalSetup();
        _clientHelper = ClientHelper.CreateClient(KestrelPort, HttpVersionValue);
        _tasks = new Task[ConcurrencyLevel];
        WarmupRequest().GetAwaiter().GetResult();
    }

    /// <summary>Disposes the <see cref="ITurboHttpClient"/> and tears down the shared server.</summary>
    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        await _clientHelper.DisposeAsync();
        await base.GlobalCleanup();
    }

    /// <inheritdoc />
    public override async Task WarmupRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/benchmark/simple");
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Issues <see cref="BenchmarkBaseClass.ConcurrencyLevel"/> concurrent GET requests to
    /// <c>/benchmark/simple</c> and waits for all to complete.
    /// </summary>
    [Benchmark]
    public Task ConcurrentRequests_Light()
    {
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            _tasks[i] = SendLightRequest();
        }

        return Task.WhenAll(_tasks);
    }

    /// <summary>
    /// Issues <see cref="BenchmarkBaseClass.ConcurrencyLevel"/> concurrent POST requests,
    /// each carrying a 10 KB body, and waits for all to complete.
    /// </summary>
    [Benchmark]
    public Task ConcurrentRequests_Heavy()
    {
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            _tasks[i] = SendHeavyRequest();
        }

        return Task.WhenAll(_tasks);
    }

    private async Task SendLightRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/benchmark/simple");
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendHeavyRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/benchmark/payload");
        request.Content = new ByteArrayContent(HeavyPayload);
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }
}