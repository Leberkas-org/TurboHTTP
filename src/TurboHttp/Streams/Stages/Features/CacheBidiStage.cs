using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Streams.Stages.Features;

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
///     <b>2xx (cacheable)</b> — stores the response via <see cref="HttpCacheStore.Put"/>.
///   </description></item>
///   <item><description>
///     <b>Unsafe method</b> — invalidates the cache entry for the request URI.
///   </description></item>
/// </list>
/// </para>
/// When no <see cref="HttpCacheStore"/> is provided the stage is a pass-through in both directions.
/// </summary>
internal sealed class CacheBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly HttpCacheStore? _store;
    private readonly CachePolicy _policy;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("Cache.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Cache.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Cache.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Cache.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public CacheBidiStage(HttpCacheStore? store, CachePolicy? policy = null)
    {
        _store = store;
        _policy = policy ?? CachePolicy.Default;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private enum CacheState
    {
        Idle,
        Forwarded,
        HitBuffered
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly CacheBidiStage _stage;
        private CacheState _state = CacheState.Idle;
        private HttpResponseMessage? _bufferedHitResponse;
        private Action<(HttpResponseMessage response, byte[] body)>? _onBodyRead;

        public Logic(CacheBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;

            // Request direction: cache lookup
            SetHandler(stage._inRequest,
                onPush: OnRequestPush,
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: ex => Log.Warning("CacheBidiStage: Request upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outRequest,
                onPull: () =>
                {
                    if (_state == CacheState.Idle && !HasBeenPulled(stage._inRequest))
                    {
                        Pull(stage._inRequest);
                    }
                },
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            // Response direction: cache storage
            SetHandler(stage._inResponse,
                onPush: OnResponsePush,
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: ex => Log.Warning("CacheBidiStage: Response upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outResponse,
                onPull: () =>
                {
                    if (_state == CacheState.HitBuffered)
                    {
                        Push(stage._outResponse, _bufferedHitResponse!);
                        _bufferedHitResponse = null;
                        _state = CacheState.Idle;
                        MaybePullNextRequest();
                    }
                    else
                    {
                        // In both Idle and Forwarded states, pull In2 to allow responses
                        // to flow. In the real pipeline In2 only delivers data after a
                        // request has been forwarded on Out1; pulling early is harmless.
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
            _onBodyRead = GetAsyncCallback<(HttpResponseMessage response, byte[] body)>(tuple =>
            {
                var (response, body) = tuple;
                var request = response.RequestMessage!;
                var now = DateTimeOffset.UtcNow;
                _stage._store!.Put(request, response, body, now, now);
                Push(_stage._outResponse, response);
                _state = CacheState.Idle;
                MaybePullNextRequest();
            });
        }

        private void OnRequestPush()
        {
            var request = Grab(_stage._inRequest);

            if (_stage._store is null)
            {
                Push(_stage._outRequest, request);
                _state = CacheState.Forwarded;
                return;
            }

            var entry = _stage._store.Get(request);
            var result = CacheFreshnessEvaluator.Evaluate(entry, request, DateTimeOffset.UtcNow, _stage._policy);

            if (result.Status is CacheLookupStatus.Fresh or CacheLookupStatus.Stale)
            {
                // RFC 9111 §5.1 — inject Age header on every cached response
                var cachedResponse = result.Entry!.Response;
                CacheFreshnessEvaluator.InjectAgeHeader(cachedResponse, result.Entry, DateTimeOffset.UtcNow);

                // RFC 9111 §5.2.2.3 — strip qualified no-cache fields before serving
                StripNoCacheFields(cachedResponse, result.Entry.CacheControl);

                // Cache hit — short-circuit to response output
                if (IsAvailable(_stage._outResponse))
                {
                    Push(_stage._outResponse, cachedResponse);
                    // Stay Idle, pull next request if engine has demand
                    MaybePullNextRequest();
                }
                else
                {
                    _bufferedHitResponse = cachedResponse;
                    _state = CacheState.HitBuffered;
                }
            }
            else
            {
                // Miss or MustRevalidate — forward to engine
                var outgoing = result is { Status: CacheLookupStatus.MustRevalidate, Entry: not null }
                    ? CacheValidationRequestBuilder.BuildConditionalRequest(request, result.Entry)
                    : request;
                Push(_stage._outRequest, outgoing);
                _state = CacheState.Forwarded;
            }
        }

        private void OnResponsePush()
        {
            var response = Grab(_stage._inResponse);

            if (_stage._store is null || response.RequestMessage is null)
            {
                Push(_stage._outResponse, response);
                _state = CacheState.Idle;
                MaybePullNextRequest();
                return;
            }

            var (processed, needsAsyncRead) = ProcessResponse(response);

            if (!needsAsyncRead)
            {
                Push(_stage._outResponse, processed);
                _state = CacheState.Idle;
                MaybePullNextRequest();
            }
            // When needsAsyncRead is true, the async callback will push downstream.
        }

        /// <summary>
        /// Processes a response for caching. Returns the response to push and whether an async
        /// body read was initiated. When <c>needsAsyncRead</c> is true, the caller must NOT push
        /// — the async callback will push after the body has been read.
        /// </summary>
        private (HttpResponseMessage response, bool needsAsyncRead) ProcessResponse(HttpResponseMessage response)
        {
            var request = response.RequestMessage!;
            var method = request.Method;
            var isUnsafe = method == HttpMethod.Post
                           || method == HttpMethod.Put
                           || method == HttpMethod.Delete
                           || method == HttpMethod.Patch;

            if (isUnsafe)
            {
                // RFC 9111 §4.4 — invalidate stored entries after unsafe method
                if (request.RequestUri is not null)
                {
                    _stage._store!.Invalidate(request.RequestUri);
                }

                return (response, false);
            }

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // RFC 9111 §4.3.4 — merge 304 with cached entry and push 200 downstream
                var entry = _stage._store!.Get(request);
                if (entry is not null)
                {
                    var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(response, entry);
                    merged.RequestMessage = request;

                    var now = DateTimeOffset.UtcNow;
                    _stage._store!.Put(request, merged, entry.Body, now, now);

                    return (merged, false);
                }

                return (response, false);
            }

            if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
            {
                // RFC 9111 §3 — store cacheable 2xx responses.
                // Sync fast-path: ReadAsByteArrayAsync on ByteArrayContent completes synchronously.
                var task = response.Content.ReadAsByteArrayAsync();

                if (task.IsCompletedSuccessfully)
                {
                    var body = task.Result;
                    var now = DateTimeOffset.UtcNow;
                    _stage._store!.Put(request, response, body, now, now);
                    return (response, false);
                }

                // Async fallback: content not yet available — schedule callback.
                var callback = _onBodyRead!;
                var capturedResponse = response;
                task.ContinueWith(t =>
                {
                    callback((capturedResponse, t.Result));
                }, TaskContinuationOptions.ExecuteSynchronously);

                return (response, true);
            }

            return (response, false);
        }

        /// <summary>
        /// RFC 9111 §5.2.2.3 — Strips header fields listed in no-cache="field1, field2"
        /// from the response before serving from cache.
        /// </summary>
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

        private void MaybePullNextRequest()
        {
            if (IsAvailable(_stage._outRequest) && !HasBeenPulled(_stage._inRequest))
            {
                Pull(_stage._inRequest);
            }
        }
    }
}
