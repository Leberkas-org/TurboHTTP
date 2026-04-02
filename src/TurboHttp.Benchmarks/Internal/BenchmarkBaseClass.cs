using BenchmarkDotNet.Attributes;

namespace TurboHttp.Benchmarks.Internal;

/// <summary>
/// Shared base class for all comparative benchmarks. Provides common
/// BenchmarkDotNet parameter sets (concurrency level, payload type, HTTP version)
/// and reusable helpers for URI construction, deterministic payload generation,
/// request warm-up, and shared Kestrel server lifecycle management.
/// </summary>
[Config(typeof(EngineBenchmarkConfig))]
public abstract class BenchmarkBaseClass
{
    /// <summary>Static shared Kestrel server instance, initialized once per test run.</summary>
    private static BenchmarkServer? _sharedServer;
    private static readonly Lock _serverLock = new();
    private static int _serverRefCount;

    /// <summary>Number of concurrent requests to issue per benchmark iteration.</summary>
    [Params(1, 4, 16, 64, 256)]
    public int ConcurrencyLevel { get; set; } = 1;

    /// <summary>
    /// Payload variant: "light" means no request body (~20-byte response),
    /// "heavy" means a 10 KB request body.
    /// </summary>
    [Params("light", "heavy")]
    public string PayloadType { get; set; } = "light";

    /// <summary>HTTP protocol version: "1.1" or "2.0".</summary>
    [Params("1.1", "2.0")]
    public string HttpVersion { get; set; } = "1.1";

    /// <summary>Port on which the HTTP/1.1 Kestrel listener is running. Set in GlobalSetup.</summary>
    protected int KestrelHttp11Port { get; set; }

    /// <summary>Port on which the HTTP/2 cleartext Kestrel listener is running. Set in GlobalSetup.</summary>
    protected int KestrelHttp20Port { get; set; }

    /// <summary>
    /// Resolves the string <see cref="HttpVersion"/> parameter to the corresponding
    /// <see cref="Version"/> instance used by <see cref="System.Net.Http.HttpRequestMessage"/>.
    /// </summary>
    public Version HttpVersionValue => HttpVersion switch
    {
        "2.0" => System.Net.HttpVersion.Version20,
        _ => System.Net.HttpVersion.Version11
    };

    /// <summary>
    /// Returns the port for the current <see cref="HttpVersion"/> parameter.
    /// HTTP/2 benchmarks must connect to the h2c-only listener; HTTP/1.1 benchmarks
    /// to the HTTP/1.1 listener.
    /// </summary>
    protected int KestrelPort => HttpVersion == "2.0" ? KestrelHttp20Port : KestrelHttp11Port;

    /// <summary>
    /// Returns the base URI for the Kestrel test server at the given <paramref name="path"/>.
    /// Uses the loopback address 127.0.0.1 and the port appropriate for the current HttpVersion.
    /// </summary>
    public Uri CreateKestrelUri(string path) =>
        new($"http://127.0.0.1:{KestrelPort}{path}");

    /// <summary>
    /// Returns a deterministic byte array of exactly <paramref name="sizeBytes"/> bytes.
    /// The pattern is a repeating 0..255 sequence, so two calls with the same argument
    /// always produce byte-for-byte identical output.
    /// </summary>
    public static byte[] GeneratePayload(int sizeBytes)
    {
        var payload = new byte[sizeBytes];
        for (var i = 0; i < sizeBytes; i++)
        {
            payload[i] = (byte)(i % 256);
        }

        return payload;
    }

    /// <summary>
    /// Sends a warm-up request to the test server so that connection setup and
    /// JIT overhead are excluded from measured iterations.
    /// Derived classes override this to perform a real HTTP round-trip.
    /// The base implementation is a no-op; it is safe to call before the server starts.
    /// </summary>
    public virtual Task WarmupRequest() => Task.CompletedTask;

    /// <summary>
    /// Initializes the shared Kestrel server on first call. Subsequent calls increment
    /// a reference counter and reuse the existing server. Called by BenchmarkDotNet
    /// once per benchmark instance during setup.
    /// </summary>
    public virtual void GlobalSetup()
    {
        lock (_serverLock)
        {
            if (_sharedServer is null)
            {
                _sharedServer = new BenchmarkServer();
                _sharedServer.InitializeAsync().GetAwaiter().GetResult();
            }

            _serverRefCount++;
            KestrelHttp11Port = _sharedServer.Http11Port;
            KestrelHttp20Port = _sharedServer.Http20Port;
        }
    }

    /// <summary>
    /// Cleans up the shared Kestrel server when the last benchmark instance finishes.
    /// Called by BenchmarkDotNet once per benchmark instance during cleanup.
    /// </summary>
    public virtual async Task GlobalCleanup()
    {
        lock (_serverLock)
        {
            _serverRefCount--;
            if (_serverRefCount == 0 && _sharedServer is not null)
            {
                _sharedServer.DisposeAsync().GetAwaiter().GetResult();
                _sharedServer = null;
            }
        }

        await Task.CompletedTask;
    }
}
