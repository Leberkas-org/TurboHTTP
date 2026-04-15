using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
using Akka.Actor;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Lifecycle;

namespace TurboHTTP;

/// <summary>
/// Pooled per-request completion source backed by <see cref="ManualResetValueTaskSourceCore{T}"/>.
/// Avoids the ~120 B per-request allocation of <see cref="TaskCompletionSource{T}"/> + its inner <see cref="Task{T}"/>.
/// Completed directly by the pipeline Sink via <see cref="TcsCorrelation.Key"/> (G2).
/// Pooled on a static <see cref="System.Collections.Concurrent.ConcurrentStack{T}"/> for reuse (E4).
/// </summary>
internal sealed class PendingRequest : IValueTaskSource<HttpResponseMessage>
{
    private static readonly ConcurrentStack<PendingRequest> Pool = new();

    private ManualResetValueTaskSourceCore<HttpResponseMessage> _core = new() { RunContinuationsAsynchronously = true };

    private PendingRequest()
    {
    }

    public static PendingRequest Rent()
    {
        if (!Pool.TryPop(out var item)) return new PendingRequest();
        item._core.Reset();
        return item;
    }

    public static void Return(PendingRequest item) => Pool.Push(item);

    /// <summary>The version token from the current MRVTSC cycle — used by the Sink to guard against stale completions.</summary>
    public short Version => _core.Version;

    /// <summary>Returns a <see cref="ValueTask{T}"/> tied to the current cycle of this instance.</summary>
    public ValueTask<HttpResponseMessage> GetValueTask() => new(this, _core.Version);

    public bool TrySetResult(HttpResponseMessage response, short expectedVersion)
    {
        if (_core.Version != expectedVersion)
        {
            return false;
        }

        try
        {
            _core.SetResult(response);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool TrySetException(Exception exception)
    {
        try
        {
            _core.SetException(exception);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool TrySetCanceled(CancellationToken ct = default) => TrySetException(new OperationCanceledException(ct));

    public HttpResponseMessage GetResult(short token) => _core.GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token,
        ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}

public sealed class TurboHttpClient : ITurboHttpClient
{
    private readonly HttpRequestMessage _defaultHeadersHolder = new();

    // Lock-free tracking for CancelPendingRequests — avoids lock contention on the hot path.
    private readonly ConcurrentDictionary<PendingRequest, byte> _pendingTcs = new();

    // Pooled CancellationTokenSources — reused via TryReset() to avoid per-request allocation.
    // Only used for non-linked CTS (no caller CT). Capped to avoid unbounded growth.
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
        NetworkBuffer.ConfigurePoolSize(512);
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
                else if (Interlocked.Increment(ref _ctsPoolCount) <= 64)
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