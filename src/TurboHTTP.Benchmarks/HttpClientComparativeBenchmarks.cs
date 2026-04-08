using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks;

/// <summary>
/// Baseline benchmarks measuring standard .NET <see cref="System.Net.Http.HttpClient"/>
/// performance for a single sequential request across light (no body) and heavy (10KB body)
/// payloads. Parameterized by HTTP version (1.1 and 2.0).
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(5)]
[InvocationCount(32)]
public class HttpClientSingleRequestBenchmarks : BenchmarkBaseClass
{
    private HttpClient _httpClient = null!;
    private static readonly byte[] HeavyPayload = GeneratePayload(10 * 1024);

    /// <summary>
    /// Creates an <see cref="HttpClient"/> configured for the current HTTP version
    /// with auto-redirect disabled, then warms it up with a single request.
    /// </summary>
    [GlobalSetup]
    public override void GlobalSetup()
    {
        base.GlobalSetup();

        // Required for HTTP/2 over cleartext (h2c) on loopback
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            EnableMultipleHttp2Connections = true,
        };

        _httpClient = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersionValue,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        WarmupRequest().GetAwaiter().GetResult();
    }

    /// <summary>Disposes the <see cref="HttpClient"/> and tears down the shared server.</summary>
    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        _httpClient.Dispose();
        await base.GlobalCleanup();
    }

    /// <inheritdoc />
    public override async Task WarmupRequest()
    {
        using var response = await _httpClient.GetAsync(CreateKestrelUri("/benchmark/simple"));
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Issues a single GET request to <c>/benchmark/simple</c> and discards the response.
    /// Measures per-request overhead for the minimal (no body) payload scenario.
    /// </summary>
    [Benchmark]
    public async Task SingleRequest_Light()
    {
        using var response = await _httpClient.GetAsync(CreateKestrelUri("/benchmark/simple"));
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Issues a single POST request with a 10 KB body to <c>/benchmark/payload</c>.
    /// Measures per-request overhead for the heavy payload scenario.
    /// </summary>
    [Benchmark]
    public async Task SingleRequest_Heavy()
    {
        using var content = new ByteArrayContent(HeavyPayload);
        using var response = await _httpClient.PostAsync(CreateKestrelUri("/benchmark/payload"), content);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Baseline benchmarks measuring standard .NET <see cref="System.Net.Http.HttpClient"/>
/// performance under concurrent load. N requests are fired concurrently using
/// <see cref="Task.WhenAll"/> and awaited as a unit. Parameterized by
/// <see cref="BenchmarkBaseClass.ConcurrencyLevel"/> and HTTP version.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(5)]
[InvocationCount(32)]
public class HttpClientConcurrentBenchmarks : BenchmarkBaseClass
{
    private HttpClient _httpClient = null!;
    private static readonly byte[] HeavyPayload = GeneratePayload(10 * 1024);

    // Pre-allocated per parameter combination — avoids Task[] heap allocation inside the hot path.
    private Task[] _tasks = null!;

    /// <summary>
    /// Creates an <see cref="HttpClient"/> configured for the current HTTP version with
    /// auto-redirect disabled and a generous connection limit to support all concurrency
    /// levels, then warms it up with a single request.
    /// </summary>
    [GlobalSetup]
    public override void GlobalSetup()
    {
        base.GlobalSetup();

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

        _tasks = new Task[ConcurrencyLevel];
        WarmupRequest().GetAwaiter().GetResult();
    }

    /// <summary>Disposes the <see cref="HttpClient"/> and tears down the shared server.</summary>
    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        _httpClient.Dispose();
        await base.GlobalCleanup();
    }

    /// <inheritdoc />
    public override async Task WarmupRequest()
    {
        using var response = await _httpClient.GetAsync(CreateKestrelUri("/benchmark/simple"));
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
        using var response = await _httpClient.GetAsync(CreateKestrelUri("/benchmark/simple"));
        response.EnsureSuccessStatusCode();
    }

    private async Task SendHeavyRequest()
    {
        using var content = new ByteArrayContent(HeavyPayload);
        using var response = await _httpClient.PostAsync(CreateKestrelUri("/benchmark/payload"), content);
        response.EnsureSuccessStatusCode();
    }
}
