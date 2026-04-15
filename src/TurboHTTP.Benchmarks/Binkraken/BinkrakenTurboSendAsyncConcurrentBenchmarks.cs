using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.Binkraken;

/// <summary>
/// Benchmarks measuring <see cref="ITurboHttpClient"/> performance using
/// <see cref="ITurboHttpClient.SendAsync"/> under concurrent load against
/// Binkraken.com over HTTPS.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class BinkrakenTurboSendAsyncConcurrentBenchmarks : BinkrakenBaseClass
{
    private const int MaxFanOut = 1024;

    [Params(1, 512, 4096)]
    public int ConcurrencyLevel { get; set; }

    private static readonly Uri BaseAddress = new("https://binkraken.com");

    private ClientHelper _clientHelper = null!;
    private Task[] _tasks = null!;
    private SemaphoreSlim _fanOutGate = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _clientHelper = ClientHelper.CreateClient(BaseAddress, HttpVersionValue);
        _tasks = new Task[ConcurrencyLevel];
        _fanOutGate = new SemaphoreSlim(MaxFanOut, MaxFanOut);
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        _fanOutGate.Dispose();
        await _clientHelper.DisposeAsync();
        await base.GlobalCleanup();
    }

    /// <inheritdoc />
    public override async Task WarmupRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LightUri);
        using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
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
            using var request = new HttpRequestMessage(HttpMethod.Get, LightUri);
            using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
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
            using var request = new HttpRequestMessage(HttpMethod.Get, HeavyUri);
            using var response = await _clientHelper.Client.SendAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            _fanOutGate.Release();
        }
    }
}
