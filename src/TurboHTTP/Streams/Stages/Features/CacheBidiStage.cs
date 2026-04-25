using System.Buffers;
using System.Net;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Diagnostics;
using TurboHTTP.Protocol.Caching;

namespace TurboHTTP.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that performs cache lookup on the request path and cache storage
/// on the response path (RFC 9111).
/// <para>
/// <b>Request direction (In1→Out1 / Out2):</b>
/// <list type="bullet">
///   <item><description>
///     <b>Cache miss</b> — the original request is forwarded on Out1 (to the engine).
///   </description></item>
///   <item><description>
///     <b>Cache hit (fresh/stale)</b> — the cached response is pushed directly on Out2
///     (short-circuit), bypassing the engine entirely. If Out2 has no demand yet the
///     response is buffered internally until demand arrives.
///   </description></item>
///   <item><description>
///     <b>Must-revalidate</b> — a conditional request (If-None-Match / If-Modified-Since)
///     is built via <see cref="CacheValidationRequestBuilder"/> and forwarded on Out1.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Response direction (In2→Out2):</b>
/// <list type="bullet">
///   <item><description>
///     <b>304 Not Modified</b> — merges headers with the cached entry and pushes 200 OK.
///   </description></item>
///   <item><description>
///     <b>2xx (cacheable)</b> — stores the response via <see cref="Cache.Put"/>.
///   </description></item>
///   <item><description>
///     <b>Unsafe method</b> — invalidates the cache entry for the request URI.
///   </description></item>
/// </list>
/// </para>
/// When no <see cref="Cache"/> is provided the stage is a pass-through in both directions.
/// </summary>
internal sealed class CacheBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    internal static readonly HttpRequestOptionsKey<bool> RevalidationKey
        = new("TurboHTTP.CacheRevalidation");

    private readonly Cache? _store;
    private readonly CachePolicy _policy;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("Cache.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Cache.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Cache.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Cache.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape
    {
        get;
    }

    public CacheBidiStage(Cache? store, CachePolicy? policy = null)
    {
        _store = store;
        _policy = policy ?? CachePolicy.Default;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new CacheBidiLogic(this);

    private sealed class CacheBidiLogic : GraphStageLogic, IFeatureStageOperations
    {
        private readonly CacheBidiStage _stage;
        private readonly CacheStateMachine _sm;
        private IActorRef _stageActorRef = ActorRefs.Nobody;

        public CacheBidiLogic(CacheBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _sm = new CacheStateMachine(this, stage._store, stage._policy);

            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    _sm.OnRequest(request);
                },
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: ex =>
                {
                    Log.Warning("CacheBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outRequest);
                });

            SetHandler(stage._outRequest,
                onPull: () =>
                {
                    if (_sm.State == CacheStateMachine.CacheState.Idle && !HasBeenPulled(stage._inRequest))
                    {
                        Pull(stage._inRequest);
                    }
                },
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    _sm.OnResponse(response);
                },
                onUpstreamFinish: () =>
                {
                    if (_sm.PendingAsyncCount > 0)
                    {
                        _sm.DeferCompletion();
                    }
                    else
                    {
                        Complete(stage._outResponse);
                    }
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("CacheBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outResponse);
                });

            SetHandler(stage._outResponse,
                onPull: () =>
                {
                    if (_sm.State == CacheStateMachine.CacheState.HitBuffered)
                    {
                        _sm.FlushBufferedHit();
                    }
                    else
                    {
                        if (!HasBeenPulled(stage._inResponse))
                        {
                            Pull(stage._inResponse);
                        }
                    }
                },
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

        public override void PreStart()
        {
            _stageActorRef = GetStageActor(OnReceive).Ref;
            _sm.SetStageActorRef(_stageActorRef);
        }

        private void OnReceive((IActorRef sender, object message) args)
        {
            _sm.OnStageActorMessage(args.message);
        }

        void IFeatureStageOperations.OnPushRequest(HttpRequestMessage request)
        {
            Push(_stage._outRequest, request);
        }

        void IFeatureStageOperations.OnPushResponse(HttpResponseMessage response)
        {
            Push(_stage._outResponse, response);
            MaybePullNextRequest();
        }

        void IFeatureStageOperations.OnSignalPullRequest()
        {
            MaybePullNextRequest();
        }

        void IFeatureStageOperations.OnSignalPullResponse()
        {
            if (_sm.State == CacheStateMachine.CacheState.HitBuffered && IsAvailable(_stage._outResponse))
            {
                _sm.FlushBufferedHit();
            }
            else if (!HasBeenPulled(_stage._inResponse) && !IsClosed(_stage._inResponse))
            {
                Pull(_stage._inResponse);
            }
        }

        void IFeatureStageOperations.OnCompleteStage()
        {
            Complete(_stage._outResponse);
        }

        void IFeatureStageOperations.OnScheduleTimer(string key, TimeSpan delay)
        {
        }

        void IFeatureStageOperations.OnCancelTimer(string key)
        {
        }

        ILoggingAdapter IFeatureStageOperations.Log => Log;

        private void MaybePullNextRequest()
        {
            if (IsAvailable(_stage._outRequest)
                && !HasBeenPulled(_stage._inRequest)
                && !IsClosed(_stage._inRequest))
            {
                Pull(_stage._inRequest);
            }
        }
    }
}

internal sealed class CacheStateMachine
{
    internal enum CacheState
    {
        Idle,
        Forwarded,
        HitBuffered,
        AwaitingCacheStore
    }

    private sealed record BodyReadComplete(HttpResponseMessage Response, IMemoryOwner<byte> Owner, int Length);

    private sealed record BodyReadFailed(Exception Exception);

    private readonly IFeatureStageOperations _ops;
    private readonly Cache? _store;
    private readonly CachePolicy _policy;

    private HttpResponseMessage? _bufferedHitResponse;
    private HttpResponseMessage? _pendingCacheResponse;
    private bool _completionDeferred;
    private IActorRef _stageActorRef = ActorRefs.Nobody;

    public CacheState State { get; private set; } = CacheState.Idle;

    public int PendingAsyncCount { get; private set; }

    public CacheStateMachine(
        IFeatureStageOperations ops,
        Cache? store,
        CachePolicy policy)
    {
        _ops = ops;
        _store = store;
        _policy = policy;
    }

    public void SetStageActorRef(IActorRef actorRef)
    {
        _stageActorRef = actorRef;
    }

    public void DeferCompletion()
    {
        _completionDeferred = true;
    }

    public void OnStageActorMessage(object message)
    {
        switch (message)
        {
            case BodyReadComplete msg:
                {
                    var request = msg.Response.RequestMessage!;
                    var now = DateTimeOffset.UtcNow;
                    _store!.Put(request, msg.Response, msg.Owner, msg.Length, now, now);
                    FlushPendingCacheResponse();
                    DecrementPendingAsync();
                    break;
                }

            case BodyReadFailed msg:
                _ops.Log.Warning("CacheBidiStage: Async body read failed: {0}", msg.Exception.Message);
                FlushPendingCacheResponse();
                DecrementPendingAsync();
                break;
        }
    }

    public void OnRequest(HttpRequestMessage request)
    {
        if (_store is null)
        {
            _ops.OnPushRequest(request);
            State = CacheState.Forwarded;
            return;
        }

        var entry = _store.Get(request);
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, DateTimeOffset.UtcNow, _policy);
        var isHit = result.Status is CacheLookupStatus.Fresh or CacheLookupStatus.Stale;

        EmitCacheTelemetry(request, isHit);

        if (isHit)
        {
            HandleCacheHit(request, result);
        }
        else
        {
            HandleCacheMiss(request, result);
        }
    }

    public void OnResponse(HttpResponseMessage response)
    {
        if (_store is null || response.RequestMessage is null)
        {
            _ops.OnPushResponse(response);
            State = CacheState.Idle;
            return;
        }

        var processed = ProcessResponse(response);
        if (State == CacheState.AwaitingCacheStore)
        {
            return;
        }

        _ops.OnPushResponse(processed);
        State = CacheState.Idle;
    }

    public void FlushBufferedHit()
    {
        _ops.OnPushResponse(_bufferedHitResponse!);
        _bufferedHitResponse = null;
        State = CacheState.Idle;
    }

    private void FlushPendingCacheResponse()
    {
        if (_pendingCacheResponse is null)
        {
            return;
        }

        var response = _pendingCacheResponse;
        _pendingCacheResponse = null;
        _ops.OnPushResponse(response);
        State = CacheState.Idle;
    }

    private void DecrementPendingAsync()
    {
        PendingAsyncCount--;
        if (PendingAsyncCount == 0 && _completionDeferred)
        {
            _ops.OnCompleteStage();
        }
    }

    private void EmitCacheTelemetry(HttpRequestMessage request, bool isHit)
    {
        if (request.Options.TryGetValue(TurboHttpInstrumentation.RequestActivityKey, out var rootActivity)
            && request.RequestUri is not null)
        {
            TurboHttpInstrumentation.AddCacheLookupEvent(rootActivity, request.RequestUri, isHit);
        }

        var result = isHit ? "hit" : "miss";
        TurboHttpMetrics.CacheLookup.Add(1,
            new KeyValuePair<string, object?>("cache.result", result));

        var uri = request.RequestUri?.OriginalString ?? "";
        TurboTrace.Cache.Info(_ops, "Cache {0}: {1}", result, uri);
    }

    private void HandleCacheHit(HttpRequestMessage request, CacheLookupResult result)
    {
        var cachedResponse = CloneCachedResponse(result.Entry!);

        CacheFreshnessEvaluator.InjectAgeHeader(cachedResponse, result.Entry!, DateTimeOffset.UtcNow);

        StripNoCacheFields(cachedResponse, result.Entry!.CacheControl);

        cachedResponse.RequestMessage = request;

        _bufferedHitResponse = cachedResponse;
        State = CacheState.HitBuffered;
        _ops.OnSignalPullResponse();
    }

    private void HandleCacheMiss(HttpRequestMessage request, CacheLookupResult result)
    {
        var isRevalidation = result is { Status: CacheLookupStatus.MustRevalidate, Entry: not null };
        var outgoing = isRevalidation
            ? CacheValidationRequestBuilder.BuildConditionalRequest(request, result.Entry!)
            : request;

        if (isRevalidation)
        {
            outgoing.Options.Set(CacheBidiStage.RevalidationKey, true);
        }

        _ops.OnPushRequest(outgoing);
        State = CacheState.Forwarded;
    }

    private HttpResponseMessage ProcessResponse(HttpResponseMessage response)
    {
        var request = response.RequestMessage!;
        var method = request.Method;
        var isUnsafe = method == HttpMethod.Post
                       || method == HttpMethod.Put
                       || method == HttpMethod.Delete
                       || method == HttpMethod.Patch;

        if (isUnsafe)
        {
            var statusCode = (int)response.StatusCode;
            if (statusCode is >= 200 and < 400 && request.RequestUri is not null)
            {
                _store!.Invalidate(request.RequestUri);

                InvalidateIfSameOrigin(request.RequestUri, response.Headers.Location);

                if (response.Content.Headers.ContentLocation is { } contentLocation)
                {
                    InvalidateIfSameOrigin(request.RequestUri, contentLocation);
                }
            }

            return response;
        }

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            var entry = _store!.Get(request);
            if (entry is not null)
            {
                var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(response, entry);
                merged.RequestMessage = request;

                var (owner, length) = Cache.RentBody(entry.Body.Span);
                var now = DateTimeOffset.UtcNow;
                _store!.Put(request, merged, owner, length, now, now);

                return merged;
            }

            return response;
        }

        if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
        {
            var task = ReadBodyToPoolAsync(response);

            _pendingCacheResponse = response;
            State = CacheState.AwaitingCacheStore;
            PendingAsyncCount++;
            task.PipeTo(_stageActorRef,
                success: result => result,
                failure: ex => new BodyReadFailed(
                    ex.GetBaseException()));
            return response;
        }

        return response;
    }

    private static void StripNoCacheFields(HttpResponseMessage response, CacheControl? cc)
    {
        if (cc?.NoCacheFields is not { Count: > 0 } fields)
        {
            return;
        }

        foreach (var field in fields)
        {
            response.Headers.Remove(field);
            response.Content?.Headers.Remove(field);
        }
    }

    private void InvalidateIfSameOrigin(Uri requestUri, Uri? targetUri)
    {
        if (targetUri is null)
        {
            return;
        }

        if (!targetUri.IsAbsoluteUri)
        {
            targetUri = new Uri(requestUri, targetUri);
        }

        if (string.Equals(requestUri.Scheme, targetUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(requestUri.Host, targetUri.Host, StringComparison.OrdinalIgnoreCase)
            && requestUri.Port == targetUri.Port)
        {
            _store!.Invalidate(targetUri);
        }
    }

    private static async Task<BodyReadComplete> ReadBodyToPoolAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var length = (int)stream.Length;
        var owner = MemoryPool<byte>.Shared.Rent(length);
        stream.ReadExactly(owner.Memory.Span[..length]);
        return new BodyReadComplete(response, owner, length);
    }

    private static HttpResponseMessage CloneCachedResponse(ICacheEntry entry)
    {
        var original = entry.Response;
        var clone = new HttpResponseMessage(original.StatusCode)
        {
            Version = original.Version,
            ReasonPhrase = original.ReasonPhrase,
            Content = new ByteArrayContent(entry.Body.ToArray())
        };

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in original.Content.Headers)
        {
            clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}