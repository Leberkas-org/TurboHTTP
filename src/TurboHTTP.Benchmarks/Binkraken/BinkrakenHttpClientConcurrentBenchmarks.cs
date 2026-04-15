using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.Binkraken;

/// <summary>
/// Baseline benchmarks measuring standard .NET <see cref="HttpClient"/> performance
/// under concurrent load against Binkraken.com over HTTPS.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class BinkrakenHttpClientConcurrentBenchmarks : BinkrakenBaseClass
{
    private const int MaxFanOut = 1024;

    [Params(1, 512, 4096)]
    public int ConcurrencyLevel { get; set; }

    private HttpClient _httpClient = null!;
    private Task[] _tasks = null!;
    private SemaphoreSlim _fanOutGate = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            EnableMultipleHttp2Connections = true,
            MaxConnectionsPerServer = 64,
        };

        _httpClient = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersionValue,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        _tasks = new Task[ConcurrencyLevel];
        _fanOutGate = new SemaphoreSlim(MaxFanOut, MaxFanOut);
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        _fanOutGate.Dispose();
        _httpClient.Dispose();
        await base.GlobalCleanup();
    }

    /// <inheritdoc />
    public override async Task WarmupRequest()
    {
        using var response = await _httpClient.GetAsync(LightUri);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark]
    public Task ConcurrentRequests_Light()
    {
        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            _tasks[i] = SendLightRequest();
        }

        return Task.WhenAll(_tasks);
    }

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
        await _fanOutGate.WaitAsync();
        try
        {
            using var response = await _httpClient.GetAsync(LightUri);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            _fanOutGate.Release();
        }
    }

    private async Task SendHeavyRequest()
    {
        await _fanOutGate.WaitAsync();
        try
        {
            using var response = await _httpClient.GetAsync(HeavyUri);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            _fanOutGate.Release();
        }
    }
}
