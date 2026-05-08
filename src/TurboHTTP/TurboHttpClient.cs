using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Lifecycle;

namespace TurboHTTP;

public sealed class TurboHttpClient : ITurboHttpClient
{
    private static readonly int MaxPooledCts = Math.Max(Environment.ProcessorCount * 4, 64);

    private readonly HttpRequestMessage _defaultHeadersHolder = new();

    private readonly ConcurrentDictionary<PendingRequest, byte> _pendingTcs = new();
    private readonly NamedClientConsumerRegistration _consumerRegistration;
    private readonly CancellationTokenSource _disposeCts = new();

    private readonly ConcurrentStack<CancellationTokenSource> _ctsPool = new();
    private int _ctsPoolCount;
    private int _disposed;

    private Uri? _baseAddress;
    private Version _defaultRequestVersion;
    private HttpVersionPolicy _defaultVersionPolicy;
    private TimeSpan _timeout;

    private readonly ICredentials? _credentials;
    private readonly bool _preAuthenticate;

    public Uri? BaseAddress
    {
        get => _baseAddress;
        set
        {
            _baseAddress = value;
            UpdateCachedOptions();
        }
    }

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

    public ChannelWriter<HttpRequestMessage> Requests { get; }

    public ChannelReader<HttpResponseMessage> Responses { get; }

    internal Guid ConsumerId => _consumerRegistration.ConsumerId;

    internal TurboRequestOptions CachedOptions { get; private set; } = null!;

    private void UpdateCachedOptions()
    {
        CachedOptions = new TurboRequestOptions(
            _baseAddress,
            DefaultRequestHeaders,
            _defaultRequestVersion,
            _defaultVersionPolicy,
            _timeout,
            _credentials,
            _preAuthenticate);
    }

    internal TurboHttpClient(
        ChannelWriter<HttpRequestMessage> requests,
        ChannelReader<HttpResponseMessage> responses,
        TurboRequestOptions options,
        NamedClientConsumerRegistration consumerRegistration)
    {
        _baseAddress = options.BaseAddress;
        _defaultRequestVersion = options.DefaultRequestVersion;
        _defaultVersionPolicy = options.DefaultVersionPolicy;
        _timeout = options.Timeout;
        _credentials = options.Credentials;
        _preAuthenticate = options.PreAuthenticate;
        foreach (var header in options.DefaultRequestHeaders)
        {
            _defaultHeadersHolder.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        UpdateCachedOptions();
        Requests = requests;
        Responses = responses;
        _consumerRegistration = consumerRegistration;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();


        var pending = PendingRequest.Rent();
        var version = pending.Version;
        request.Options.Set(TurboClientCorrelation.Key, pending);
        request.Options.Set(TurboClientCorrelation.VersionKey, version);
        request.Options.Set(TurboClientCorrelation.ConsumerIdKey, ConsumerId);

        _pendingTcs.TryAdd(pending, 0);

        try
        {
            try
            {
                await Requests.WriteAsync(request, cancellationToken);
            }
            catch (ChannelClosedException)
            {
                throw CreateClientDisposedException();
            }

            if (Timeout == System.Threading.Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
            {
                return await pending.GetValueTask();
            }

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
            PendingRequest.Return(pending);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCts.Cancel();
        try
        {
            _consumerRegistration.Dispose();
            CancelPendingRequests();
        }
        finally
        {
            _disposeCts.Dispose();
        }
    }

    public void CancelPendingRequests()
    {
        foreach (var pending in _pendingTcs.Keys)
        {
            pending.TrySetCanceled();
            _pendingTcs.TryRemove(pending, out _);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw CreateClientDisposedException();
        }
    }

    private static ObjectDisposedException CreateClientDisposedException()
    {
        return new ObjectDisposedException(nameof(TurboHttpClient),
            "Cannot send request because the client has been disposed.");
    }
}