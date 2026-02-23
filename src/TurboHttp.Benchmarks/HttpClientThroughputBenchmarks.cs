using System;
using System.Buffers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboHttp.Protocol;

namespace TurboHttp.Benchmarks;

/// <summary>
/// BM-REL-THR-01: Maximum Throughput Comparison — Baseline vs TurboHttp.
///
/// Measures the maximum achievable requests per second (RPS) under reproducible
/// release conditions. Two benchmark variants run under identical conditions:
///
///   a) Baseline — standard singleton HttpClient (SocketsHttpHandler, HTTP/1.1, keep-alive).
///      Represents the reference .NET platform HTTP stack throughput.
///
///   b) Custom — TurboHttp Http11Encoder/Http11Decoder over raw TCP keep-alive connections.
///      Represents the throughput of the TurboHttp encoding/decoding pipeline.
///
/// Server: in-process Kestrel, 127.0.0.1, dynamically assigned port, returning a fixed
/// 256-byte JSON payload. No logging. HTTP/1.1. Keep-alive enabled.
///
/// Load profile: <see cref="Parallelism"/> concurrent requests per invocation.
/// [OperationsPerInvoke(Parallelism)] normalises measured time to per-request cost.
///
/// Full release configuration (production run):
///   LaunchCount=5, WarmupCount=5, IterationCount=10, MinIterationTime=30s
///   Parallelism=256 (update the constant below for a formal release run)
///
/// For CI/dry-run validation the [SimpleJob] attribute below is sufficient.
/// </summary>
[Config(typeof(MicroBenchmarkConfig))]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class HttpClientThroughputBenchmarks
{
    // ── Configuration ────────────────────────────────────────────────────────

    /// <summary>
    /// Number of concurrent requests fired per benchmark invocation.
    /// Set to 256 for the formal release throughput run.
    /// The current value (16) is chosen for CI/dry-run: it exercises all code paths
    /// with negligible resource overhead while keeping each invocation fast.
    /// </summary>
    private const int Parallelism = 16;

    // ── State ────────────────────────────────────────────────────────────────

    private IHost _server = null!;
    private HttpClient _baselineClient = null!;
    private TcpClient[] _customPool = null!;
    private NetworkStream[] _customStreams = null!;
    private Http11Decoder[] _customDecoders = null!;
    private int _port;
    private string _baseUrl = null!;

    // Fixed-size JSON payload returned by the server (~256 bytes UTF-8).
    // Chosen to match the "256-byte JSON" specification in BM-REL-THR-01.
    private static readonly byte[] ServerPayload = System.Text.Encoding.UTF8.GetBytes(
        "{\"benchmark\":\"BM-REL-THR-01\",\"variant\":\"release\",\"pad\":" +
        $"\"{new string('x', 185)}\"}}");

    // ── Lifecycle ────────────────────────────────────────────────────────────

    [GlobalSetup]
    public async Task Setup()
    {
        // ── Server ──────────────────────────────────────────────────────────
        _server = Host.CreateDefaultBuilder()
            .ConfigureLogging(x => x.ClearProviders())
            .ConfigureWebHostDefaults(web =>
            {
                web.UseKestrel();
                web.UseUrls("http://127.0.0.1:0"); // OS assigns a free port
                web.Configure(app =>
                {
                    app.Run(async ctx =>
                    {
                        ctx.Response.ContentType = "application/json";
                        ctx.Response.ContentLength = ServerPayload.Length;
                        await ctx.Response.Body.WriteAsync(ServerPayload);
                    });
                });
            })
            .Build();

        await _server.StartAsync();

        // Discover dynamically assigned port.
        var kestrel = _server.Services.GetRequiredService<IServer>();
        var addrs = kestrel.Features.Get<IServerAddressesFeature>()!;
        _port = new Uri(addrs.Addresses.First()).Port;
        _baseUrl = $"http://127.0.0.1:{_port}/";

        // ── Baseline client ──────────────────────────────────────────────────
        // Singleton HttpClient: SocketsHttpHandler, HTTP/1.1, keep-alive,
        // pool large enough to sustain Parallelism concurrent connections.
        _baselineClient = new HttpClient(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = Parallelism,
        })
        {
            DefaultRequestVersion = new Version(1, 1),
        };

        // Prime the baseline connection pool before the timed runs.
        using var warmupResponse = await _baselineClient.GetAsync(_baseUrl, HttpCompletionOption.ResponseContentRead);

        // ── Custom pool ──────────────────────────────────────────────────────
        // Pre-establish Parallelism keep-alive TCP connections to eliminate
        // connection-setup cost and OS TIME_WAIT accumulation during the
        // BenchmarkDotNet pilot phase.
        _customPool = new TcpClient[Parallelism];
        _customStreams = new NetworkStream[Parallelism];
        _customDecoders = new Http11Decoder[Parallelism];

        for (var i = 0; i < Parallelism; i++)
        {
            _customPool[i] = new TcpClient();
            await _customPool[i].ConnectAsync(IPAddress.Loopback, _port);
            _customStreams[i] = _customPool[i].GetStream();
            _customDecoders[i] = new Http11Decoder();

            // Prime each connection: avoids first-request JIT/connection overhead
            // during warmup iterations.
            await SendCustomRequestAsync(_customStreams[i], _customDecoders[i]);
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _baselineClient.Dispose();

        for (var i = 0; i < Parallelism; i++)
        {
            _customDecoders[i].Dispose();
            _customStreams[i].Dispose();
            _customPool[i].Dispose();
        }

        await _server.StopAsync();
        _server.Dispose();
    }

    // ── BM-REL-THR-01a: Baseline — standard HttpClient ───────────────────────

    /// <summary>
    /// BM-REL-THR-01a: Baseline throughput — standard HttpClient.
    /// Fires <see cref="Parallelism"/> concurrent GET requests via a singleton
    /// HttpClient (SocketsHttpHandler, HTTP/1.1, keep-alive). The full response
    /// body is read before each Task completes, ensuring connection return to pool.
    /// [OperationsPerInvoke(Parallelism)] normalises measured time to per-request cost.
    /// </summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = Parallelism)]
    public async Task BmRelThr01a_Baseline_HttpClient()
    {
        var tasks = new Task<HttpResponseMessage>[Parallelism];
        for (var i = 0; i < Parallelism; i++)
        {
            tasks[i] = _baselineClient.GetAsync(_baseUrl, HttpCompletionOption.ResponseContentRead);
        }

        var responses = await Task.WhenAll(tasks);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // ── BM-REL-THR-01b: Custom — TurboHttp encoder/decoder ───────────────────

    /// <summary>
    /// BM-REL-THR-01b: Custom throughput — TurboHttp Http11Encoder/Http11Decoder.
    /// Fires <see cref="Parallelism"/> concurrent GET requests over pre-established
    /// keep-alive TCP connections, serialising each request with Http11Encoder and
    /// parsing each response with Http11Decoder. No connection-setup overhead per
    /// invocation. Each task uses its own dedicated connection from the pool.
    /// [OperationsPerInvoke(Parallelism)] normalises measured time to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = Parallelism)]
    public async Task BmRelThr01b_Custom_TurboHttp()
    {
        var tasks = new Task[Parallelism];
        for (var i = 0; i < Parallelism; i++)
        {
            tasks[i] = SendCustomRequestAsync(_customStreams[i], _customDecoders[i]);
        }

        await Task.WhenAll(tasks);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends one HTTP/1.1 GET request on the given keep-alive connection using
    /// TurboHttp's Http11Encoder and Http11Decoder. Uses local buffers to avoid
    /// data races when called concurrently from multiple tasks.
    /// </summary>
    private async Task SendCustomRequestAsync(NetworkStream stream, Http11Decoder decoder)
    {
        var encBuf = ArrayPool<byte>.Shared.Rent(512);
        var readBuf = ArrayPool<byte>.Shared.Rent(4096);
        // Local buffers — no shared mutable state between concurrent tasks.
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl);
            var span = encBuf.AsSpan();
            var written = Http11Encoder.Encode(req, ref span);
            await stream.WriteAsync(encBuf.AsMemory(0, written));

            // Read buffer sized to hold the full response (headers + 256-byte body).

            while (true)
            {
                var n = await stream.ReadAsync(readBuf);
                if (n == 0)
                {
                    return;
                }

                if (decoder.TryDecode(readBuf.AsMemory(0, n), out _))
                {
                    return;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(encBuf);
            ArrayPool<byte>.Shared.Return(readBuf);
        }
    }
}