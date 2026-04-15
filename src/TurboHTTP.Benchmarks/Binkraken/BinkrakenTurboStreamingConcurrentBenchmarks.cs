using BenchmarkDotNet.Attributes;
using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.Binkraken;

/// <summary>
/// Benchmarks measuring <see cref="ITurboHttpClient"/> throughput using the channel-based
/// streaming API under concurrent load against Binkraken.com over HTTPS.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class BinkrakenTurboStreamingConcurrentBenchmarks : BinkrakenBaseClass
{
    [Params(1, 512, 4096)]
    public int ConcurrencyLevel { get; set; }

    private static readonly Uri BaseAddress = new("https://binkraken.com");

    private ClientHelper _clientHelper = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _clientHelper = ClientHelper.CreateStreamingClient(BaseAddress, HttpVersionValue);
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
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
    public async Task ConcurrentRequests_Light()
    {
        await StreamRequests(LightUri);
    }

    [Benchmark]
    public async Task ConcurrentRequests_Heavy()
    {
        await StreamRequests(HeavyUri);
    }

    private async Task StreamRequests(Uri uri)
    {
        var client = _clientHelper.Client;
        var count = ConcurrencyLevel;

        for (var i = 0; i < count; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            await client.Requests.WriteAsync(request);
        }

        var received = 0;
        while (received < count)
        {
            if (!await client.Responses.WaitToReadAsync())
            {
                break;
            }

            while (client.Responses.TryRead(out var response))
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
}
