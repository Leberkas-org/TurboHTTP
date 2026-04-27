using BenchmarkDotNet.Attributes;

namespace TurboHTTP.Benchmarks.Internal;

/// <summary>
/// Common base class shared by all benchmark suites (Binkraken remote and Kestrel localhost).
/// Provides BenchmarkDotNet parameter sets (concurrency level, HTTP version), ThreadPool
/// tuning, and the warm-up hook. Subclasses add environment-specific setup (server lifecycle,
/// URI construction, payload helpers).
/// </summary>
[Config(typeof(EngineBenchmarkConfig))]
public abstract class BenchmarkSuiteBase
{
    /// <summary>HTTP protocol version: "1.1", "2.0", or "3.0".</summary>
    [Params("1.1", "2.0", "3.0")]
    public string HttpVersion { get; set; } = "1.1";

    /// <summary>
    /// Resolves the string <see cref="HttpVersion"/> parameter to the corresponding
    /// <see cref="Version"/> instance used by <see cref="System.Net.Http.HttpRequestMessage"/>.
    /// </summary>
    public Version HttpVersionValue => HttpVersion switch
    {
        "3.0" => System.Net.HttpVersion.Version30,
        "2.0" => System.Net.HttpVersion.Version20,
        _ => System.Net.HttpVersion.Version11
    };

    /// <summary>
    /// Sends a warm-up request so that connection setup, DNS resolution, TLS handshake,
    /// and JIT overhead are excluded from measured iterations.
    /// Derived classes override this to perform a real HTTP round-trip.
    /// </summary>
    public virtual Task WarmupRequest() => Task.CompletedTask;

    /// <summary>
    /// Initializes the benchmark environment. Sets high minimum ThreadPool counts to
    /// ensure Akka dispatchers and SocketsHttpHandler have adequate threads.
    /// Subclasses should call <c>base.GlobalSetup()</c> first.
    /// </summary>
    public virtual Task GlobalSetup()
    {
        ThreadPool.GetMinThreads(out var w, out var io);
        ThreadPool.SetMinThreads(Math.Max(w, 1024), Math.Max(io, 1024));
        return Task.CompletedTask;
    }

    /// <summary>Cleanup hook for derived classes.</summary>
    public virtual Task GlobalCleanup() => Task.CompletedTask;
}