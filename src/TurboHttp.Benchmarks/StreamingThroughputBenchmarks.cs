using Akka.Actor;
using Akka.Configuration;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TurboHttp.Benchmarks.Internal;
using TurboHttp.Internal;

namespace TurboHttp.Benchmarks;

/// <summary>
/// Streaming throughput benchmark: pumps N requests through the pipeline as fast as possible
/// and measures total time to receive all N responses.
/// <para>
/// <b>TurboHttp</b>: Uses <see cref="ITurboHttpClient.Requests"/> (ChannelWriter) to stream requests
/// and <see cref="ITurboHttpClient.Responses"/> (ChannelReader) to collect responses — no per-request
/// Task/TCS overhead, pure pipeline throughput.
/// </para>
/// <para>
/// <b>HttpClient</b>: Uses <c>Task.WhenAll</c> with N <see cref="HttpClient.SendAsync"/> calls
/// as the closest equivalent (HttpClient has no channel-based API).
/// </para>
/// </summary>
[MemoryDiagnoser]
[WarmupCount(2)]
[IterationCount(5)]
[InvocationCount(1)]
[Config(typeof(EngineBenchmarkConfig))]
public class StreamingThroughputBenchmarks
{
    private static BenchmarkServer? _server;
    private static readonly Lock ServerLock = new();
    private static int _serverRefCount;

    private static readonly Config BenchHocon =
        TurboHttpDispatchers.CreateConfig(TurboClientOptions.DefaultMaxEndpointSubstreams);

    private ServiceProvider? _turboProvider;
    private ITurboHttpClient? _turboClient;
    private ActorSystem? _turboSystem;

    private HttpClient? _httpClient;

    private int _kestrelPort;

    /// <summary>Total number of requests to stream per invocation.</summary>
    [Params(1000, 5000, 10000)]
    public int RequestCount { get; set; } = 1000;

    /// <summary>HTTP protocol version.</summary>
    [Params("1.1")]
    public string HttpVersion { get; set; } = "1.1";

    private Version HttpVersionValue => HttpVersion switch
    {
        "2.0" => System.Net.HttpVersion.Version20,
        _ => System.Net.HttpVersion.Version11
    };

    private int KestrelPort => HttpVersion == "2.0"
        ? _server!.Http20Port
        : _server!.Http11Port;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        lock (ServerLock)
        {
            if (_server is null)
            {
                _server = new BenchmarkServer();
                _server.InitializeAsync().GetAwaiter().GetResult();
            }

            _serverRefCount++;
        }

        _kestrelPort = KestrelPort;

        SetupTurboClient();

        SetupHttpClient();

        await WarmupTurbo();
        await WarmupHttpClient();
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_turboClient is not null)
        {
            _turboClient.Requests.TryComplete();

            try
            {
                await _turboClient.Responses.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Pipeline may complete with an error during shutdown — that is fine.
            }

            _turboClient.Dispose();
        }

        if (_turboSystem is not null)
        {
            await _turboSystem.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
            await _turboSystem.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(250);
        }

        if (_turboProvider is not null)
        {
            await _turboProvider.DisposeAsync();
        }

        _httpClient?.Dispose();

        lock (ServerLock)
        {
            _serverRefCount--;
            if (_serverRefCount == 0 && _server is not null)
            {
                _server.DisposeAsync().GetAwaiter().GetResult();
                _server = null;
            }
        }
    }

    /// <summary>
    /// TurboHttp streaming: pump all requests via ChannelWriter, read all responses via ChannelReader.
    /// No per-request Task allocation — pure pipeline throughput.
    /// </summary>
    [Benchmark(Description = "TurboHttp_Streaming")]
    public async Task TurboHttp_StreamRequests()
    {
        var client = _turboClient!;
        var writer = client.Requests;
        var reader = client.Responses;
        var count = RequestCount;

        // Fire all requests as fast as the channel accepts them

        for (var i = 0; i < count; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/benchmark/simple");
            await writer.WriteAsync(request);
        }


        // Drain all responses — use manual loop instead of ReadAllAsync to avoid
        // IAsyncEnumerator disposal issues when breaking out early
        var received = 0;
        while (received < count)
        {
            if (!await reader.WaitToReadAsync())
            {
                break; // Channel completed (shouldn't happen during benchmark)
            }

            while (reader.TryRead(out var response))
            {
                response.Dispose();
                received++;
                if (received >= count)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// HttpClient baseline: fire all requests concurrently via Task.WhenAll with SendAsync.
    /// This is the closest equivalent to streaming — HttpClient has no channel API.
    /// </summary>
    [Benchmark(Description = "HttpClient_Concurrent", Baseline = true)]
    public async Task HttpClient_ConcurrentRequests()
    {
        var client = _httpClient!;
        var count = RequestCount;

        var tasks = new Task<HttpResponseMessage>[count];
        for (var i = 0; i < count; i++)
        {
            tasks[i] = client.GetAsync($"http://127.0.0.1:{_kestrelPort}/benchmark/simple");
        }

        var responses = await Task.WhenAll(tasks);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }


    private void SetupTurboClient()
    {
        var services = new ServiceCollection();

        var options = new TurboClientOptions
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_kestrelPort}"),
            DangerousAcceptAnyServerCertificate = true,
            // Single substream: all requests pipelined on one connection.
            // Multi-substream mode has a known backpressure issue under streaming load.
            MaxH1ConnectionsPerServer = 1
        };

        _turboSystem = ActorSystem.Create($"turbohttp-streaming-{Guid.NewGuid():N}", BenchHocon);
        services.AddSingleton(_turboSystem);
        services.AddTurboHttpClient();
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<TurboClientOptions>>(
            new FixedOptionsFactory(options)));

        _turboProvider = services.BuildServiceProvider();
        var factory = _turboProvider.GetRequiredService<ITurboHttpClientFactory>();
        _turboClient = factory.CreateClient(string.Empty);
        _turboClient.BaseAddress = options.BaseAddress;
        _turboClient.DefaultRequestVersion = HttpVersionValue;
        _turboClient.Timeout = TimeSpan.FromMinutes(5);
    }

    private void SetupHttpClient()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            EnableMultipleHttp2Connections = true,
            MaxConnectionsPerServer = 512,
        };

        _httpClient = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersionValue,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
    }

    private async Task WarmupTurbo()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/benchmark/simple");
        using var response = await _turboClient!.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
    }

    private async Task WarmupHttpClient()
    {
        using var response = await _httpClient!.GetAsync($"http://127.0.0.1:{_kestrelPort}/benchmark/simple");
        response.EnsureSuccessStatusCode();
    }

    private sealed class FixedOptionsFactory(TurboClientOptions options) : IOptionsFactory<TurboClientOptions>
    {
        public TurboClientOptions Create(string name) => options;
    }
}