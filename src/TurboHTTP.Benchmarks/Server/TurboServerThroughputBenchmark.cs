using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.Server;

/// <summary>
/// End-to-end server throughput benchmark for TurboHTTP running on loopback.
/// Measures request/second throughput with various concurrency levels and payload sizes.
/// Starts a minimal TurboHTTP server with simple routes and fires concurrent requests at it using HttpClient.
/// </summary>
[Config(typeof(EngineBenchmarkConfig))]
[MemoryDiagnoser]
[WarmupCount(5)]
[IterationCount(15)]
public class TurboServerThroughputBenchmark
{
    private const int MaxFanOut = 1024;

    [Params(1, 64, 256)]
    public int ConcurrencyLevel { get; set; }

    private WebApplication? _app;
    private HttpClient? _httpClient;
    private Task[] _tasks = null!;
    private SemaphoreSlim _fanOutGate = null!;
    private int _serverPort;

    /// <summary>Base address for requests against the TurboHTTP server.</summary>
    private Uri ServerUri => new($"http://127.0.0.1:{_serverPort}");

    /// <summary>Plaintext endpoint returning minimal response.</summary>
    private Uri PlaintextUri => new(ServerUri, "/plaintext");

    /// <summary>JSON endpoint returning small JSON object.</summary>
    private Uri JsonUri => new(ServerUri, "/json");

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // Configure ThreadPool for high concurrency
        ThreadPool.GetMinThreads(out var w, out var io);
        ThreadPool.SetMinThreads(Math.Max(w, 1024), Math.Max(io, 1024));

        // Enable HTTP/2 over cleartext for benchmarks
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        // Start the TurboHTTP server
        await StartServerAsync();

        // Create HttpClient for requests
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            EnableMultipleHttp2Connections = true,
            MaxConnectionsPerServer = 128,
        };

        _httpClient = new HttpClient(handler)
        {
            DefaultRequestVersion = System.Net.HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        _tasks = new Task[ConcurrencyLevel];
        _fanOutGate = new SemaphoreSlim(MaxFanOut, MaxFanOut);

        // Warmup request to initialize connection pool and JIT
        await WarmupRequest();
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _fanOutGate?.Dispose();
        _httpClient?.Dispose();
        await StopServerAsync();
    }

    /// <summary>
    /// Sequential GET /plaintext: measures single-request latency.
    /// </summary>
    [Benchmark]
    public async Task PlaintextGet_Sequential()
    {
        using var response = await _httpClient!.GetAsync(PlaintextUri);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Concurrent GET /plaintext with 64 parallel requests: measures throughput.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Concurrent")]
    public Task PlaintextGet_Concurrent()
    {
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            _tasks[i] = SendPlaintextRequest();
        }

        return Task.WhenAll(_tasks);
    }

    /// <summary>
    /// Sequential GET /json: measures latency on small JSON response.
    /// </summary>
    [Benchmark]
    public async Task JsonGet_Sequential()
    {
        using var response = await _httpClient!.GetAsync(JsonUri);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Concurrent GET /json with variable concurrency level: measures throughput on JSON responses.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Concurrent")]
    public Task JsonGet_Concurrent()
    {
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            _tasks[i] = SendJsonRequest();
        }

        return Task.WhenAll(_tasks);
    }

    private async Task SendPlaintextRequest()
    {
        await _fanOutGate.WaitAsync();
        try
        {
            using var response = await _httpClient!.GetAsync(PlaintextUri);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            _fanOutGate.Release();
        }
    }

    private async Task SendJsonRequest()
    {
        await _fanOutGate.WaitAsync();
        try
        {
            using var response = await _httpClient!.GetAsync(JsonUri);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            _fanOutGate.Release();
        }
    }

    private async Task WarmupRequest()
    {
        using var response = await _httpClient!.GetAsync(PlaintextUri);
        response.EnsureSuccessStatusCode();
    }

    private async Task StartServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();

        // Register benchmark endpoints
        app.MapGet("/plaintext", () =>
            Results.Content("Hello, World!", "text/plain"));

        app.MapGet("/json", () =>
            Results.Json(new { message = "Hello, World!" }));

        await app.StartAsync();

        // Extract the dynamically assigned port
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .FirstOrDefault();

        if (addresses is null)
        {
            throw new InvalidOperationException("Failed to extract server address");
        }

        _serverPort = new Uri(addresses).Port;
        _app = app;
    }

    private async Task StopServerAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
