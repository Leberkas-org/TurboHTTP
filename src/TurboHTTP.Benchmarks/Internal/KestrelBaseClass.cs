namespace TurboHTTP.Benchmarks.Internal;

/// <summary>
/// Base class for all Kestrel localhost benchmarks. Manages a shared Kestrel
/// test server and provides URI construction helpers for the light and heavy
/// benchmark endpoints.
/// </summary>
public abstract class KestrelBaseClass : BenchmarkSuiteBase
{
    /// <summary>Static shared Kestrel server instance, initialized once per test run.</summary>
    private static BenchmarkServer? _sharedServer;
    private static readonly SemaphoreSlim _serverLock = new(1, 1);
    private static int _serverRefCount;

    /// <summary>Heavy payload: 10 KB deterministic byte array for POST benchmarks.</summary>
    protected static readonly byte[] HeavyPayload = GeneratePayload(10 * 1024);

    /// <summary>Port on which the HTTP/1.1 Kestrel listener is running. Set in GlobalSetup.</summary>
    protected int KestrelHttp11Port { get; private set; }

    /// <summary>Port on which the HTTP/2 cleartext Kestrel listener is running. Set in GlobalSetup.</summary>
    protected int KestrelHttp20Port { get; private set; }

    /// <summary>Port on which the HTTP/3 (QUIC+TLS) Kestrel listener is running. Set in GlobalSetup.</summary>
    protected int KestrelHttp30Port { get; private set; }

    /// <summary>
    /// Returns the port for the current <see cref="BenchmarkSuiteBase.HttpVersion"/> parameter.
    /// </summary>
    protected int KestrelPort => HttpVersion switch
    {
        "3.0" => KestrelHttp30Port,
        "2.0" => KestrelHttp20Port,
        _ => KestrelHttp11Port,
    };

    private string Scheme => HttpVersion == "3.0" ? "https" : "http";

    /// <summary>
    /// Light endpoint: minimal GET returning ~3 bytes.
    /// Computed after the server starts and ports are known.
    /// </summary>
    public Uri LightUri => new($"{Scheme}://127.0.0.1:{KestrelPort}/benchmark/simple");

    /// <summary>
    /// Heavy endpoint: POST with a 10 KB body.
    /// Computed after the server starts and ports are known.
    /// </summary>
    public Uri HeavyUri => new($"{Scheme}://127.0.0.1:{KestrelPort}/benchmark/payload");

    /// <summary>
    /// Returns the base address for the Kestrel test server at the current HTTP version port.
    /// </summary>
    public Uri BaseAddress => new($"{Scheme}://127.0.0.1:{KestrelPort}");

    /// <summary>
    /// Returns a deterministic byte array of exactly <paramref name="sizeBytes"/> bytes.
    /// The pattern is a repeating 0..255 sequence.
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
    /// Initializes the shared Kestrel server on first call. Subsequent calls increment
    /// a reference counter and reuse the existing server.
    /// </summary>
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();

        await _serverLock.WaitAsync();
        try
        {
            if (_sharedServer is null)
            {
                _sharedServer = new BenchmarkServer();
                await _sharedServer.InitializeAsync();
            }

            _serverRefCount++;
            KestrelHttp11Port = _sharedServer.Http11Port;
            KestrelHttp20Port = _sharedServer.Http20Port;
            KestrelHttp30Port = _sharedServer.Http30Port;
        }
        finally
        {
            _serverLock.Release();
        }
    }

    /// <summary>
    /// Cleans up the shared Kestrel server when the last benchmark instance finishes.
    /// </summary>
    public override async Task GlobalCleanup()
    {
        await _serverLock.WaitAsync();
        try
        {
            _serverRefCount--;
            if (_serverRefCount == 0 && _sharedServer is not null)
            {
                await _sharedServer.DisposeAsync();
                _sharedServer = null;
            }
        }
        finally
        {
            _serverLock.Release();
        }
    }
}
