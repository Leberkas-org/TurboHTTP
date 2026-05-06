using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Akka.Actor;
using Servus.Akka.Transport;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Lifecycle;

namespace TurboHTTP;

public sealed class TurboHttpClient : ITurboHttpClient
{
    private static readonly int MaxPooledCts = Math.Max(Environment.ProcessorCount * 4, 64);

    private readonly HttpRequestMessage _defaultHeadersHolder = new();

    private readonly ConcurrentDictionary<PendingRequest, byte> _pendingTcs = new();

    private readonly ConcurrentStack<CancellationTokenSource> _ctsPool = new();
    private int _ctsPoolCount;

    private Uri? _baseAddress;
    private Version _defaultRequestVersion = HttpVersion.Version11;
    private HttpVersionPolicy _defaultVersionPolicy;
    private TimeSpan _timeout = TimeSpan.FromSeconds(60);

    private readonly ICredentials? _credentials;
    private readonly bool _preAuthenticate;

    // Initialized to null! here; UpdateCachedOptions() is called first in the constructor
    // (before Manager is created), so this field is always non-null when observable.
    private TurboRequestOptions _cachedOptions = null!;

    public Uri? BaseAddress
    {
        get => _baseAddress;
        set
        {
            _baseAddress = value;
            UpdateCachedOptions();
        }
    }

    /// <summary>Gets the default request headers sent with each request.</summary>
    /// <remarks>
    /// The <see cref="HttpRequestHeaders"/> instance is stored by reference in the internal
    /// <see cref="TurboRequestOptions"/> snapshot — it is NOT copied. Configure headers before
    /// submitting requests; mutating them concurrently with active requests produces undefined behavior.
    /// </remarks>
    public HttpRequestHeaders DefaultRequestHeaders => _defaultHeadersHolder.Headers;

    public Version DefaultRequestVersion
    {
        get => _defaultRequestVersion;
        set
        {
            _defaultRequestVersion = value;
            UpdateCachedOptions();
        }
    }

    public HttpVersionPolicy DefaultVersionPolicy
    {
        get => _defaultVersionPolicy;
        set
        {
            _defaultVersionPolicy = value;
            UpdateCachedOptions();
        }
    }

    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            _timeout = value;
            UpdateCachedOptions();
        }
    }

    public long MaxResponseContentBufferSize { get; set; }

    public ChannelWriter<HttpRequestMessage> Requests => Manager.Requests;
    public ChannelReader<HttpResponseMessage> Responses => Manager.Responses;
    
    internal ClientStreamManager Manager { get; }

    private void UpdateCachedOptions()
    {
        _cachedOptions = new TurboRequestOptions(
            _baseAddress,
            DefaultRequestHeaders,
            _defaultRequestVersion,
            _defaultVersionPolicy,
            _timeout,
            _credentials,
            _preAuthenticate);
    }

    internal TurboHttpClient(TurboClientOptions clientOptions, ActorSystem system, PipelineDescriptor pipeline)
    {
        _credentials = clientOptions.Credentials;
        _preAuthenticate = clientOptions.PreAuthenticate;
        UpdateCachedOptions();
        TransportBuffer.ConfigurePoolSize(512);
        Manager = new ClientStreamManager(clientOptions, OptionsFactory, system, pipeline);
        return;

        TurboRequestOptions OptionsFactory() => _cachedOptions;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        request.Options.Set(TcsCorrelation.Key, pending);
        request.Options.Set(TcsCorrelation.VersionKey, version);

        _pendingTcs.TryAdd(pending, 0);

        try
        {
            try
            {
                await Manager.Requests.WriteAsync(request, cancellationToken);
            }
            catch (ChannelClosedException)
            {
                throw new ObjectDisposedException(nameof(TurboHttpClient),
                    "Cannot send request because the client has been disposed.");
            }

            // Fast path: no timeout and no caller CT — skip CTS allocation entirely.
            if (Timeout == System.Threading.Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
            {
                return await pending.GetValueTask();
            }

            // Timeout + caller CT are combined via a linked CTS with UnsafeRegister,
            // which cancels the PendingRequest without allocating a DelayPromise.
            // Non-linked CTS is rented from pool and returned via TryReset() to avoid per-request allocation.
            var linkedCt = cancellationToken.CanBeCanceled;
            CancellationTokenSource cts;
            if (linkedCt)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            else if (_ctsPool.TryPop(out var pooled))
            {
                Interlocked.Decrement(ref _ctsPoolCount);
                cts = pooled;
            }
            else
            {
                cts = new CancellationTokenSource();
            }

            try
            {
                cts.CancelAfter(Timeout);
                await using (cts.Token.UnsafeRegister(
                                 static (state, ct) => ((PendingRequest)state!).TrySetCanceled(ct),
                                 pending))
                {
                    return await pending.GetValueTask();
                }
            }
            finally
            {
                if (linkedCt || !cts.TryReset())
                {
                    cts.Dispose();
                }
                else if (Interlocked.Increment(ref _ctsPoolCount) <= MaxPooledCts)
                {
                    _ctsPool.Push(cts);
                }
                else
                {
                    Interlocked.Decrement(ref _ctsPoolCount);
                    cts.Dispose();
                }
            }
        }
        finally
        {
            _pendingTcs.TryRemove(pending, out _);

            // Return to pool only after the ValueTask is consumed (version mismatch
            // guard in TrySetResult prevents stale pipeline completions from corrupting
            // the reused instance).
            PendingRequest.Return(pending);
        }
    }

    public void Dispose() => Manager.Dispose();

    public void CancelPendingRequests()
    {
        foreach (var pending in _pendingTcs.Keys)
        {
            pending.TrySetCanceled();
            _pendingTcs.TryRemove(pending, out _);
        }
    }
}