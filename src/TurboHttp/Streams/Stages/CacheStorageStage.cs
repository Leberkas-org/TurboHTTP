using System;
using System.Net;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// RFC 9111 §3/§4.3.4/§4.4 — Stores cacheable responses and handles revalidation.
/// <para>
/// On each incoming <see cref="HttpResponseMessage"/>:
/// <list type="bullet">
///   <item><description>
///     <b>304 Not Modified</b> — merges headers from the 304 response with the cached entry via
///     <see cref="CacheValidationRequestBuilder.MergeNotModifiedResponse"/> and pushes the resulting
///     200 OK downstream. The merged entry is also written back to the store.
///   </description></item>
///   <item><description>
///     <b>2xx (cacheable)</b> — calls <see cref="HttpCacheStore.Put"/> to store the response.
///     The body is read synchronously and stored alongside request/response timestamps.
///   </description></item>
///   <item><description>
///     <b>Unsafe method (POST/PUT/DELETE/PATCH)</b> — calls <see cref="HttpCacheStore.Invalidate"/>
///     for the request URI (RFC 9111 §4.4).
///   </description></item>
///   <item><description>
///     All responses are pushed downstream unchanged (or as merged 200 for 304 cases).
///   </description></item>
/// </list>
/// </para>
/// When <see cref="HttpResponseMessage.RequestMessage"/> is null the response is passed through unmodified.
/// </summary>
internal sealed class CacheStorageStage : GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>
{
    private readonly HttpCacheStore _store;

    private readonly Inlet<HttpResponseMessage> _inlet = new("cacheStorage.in");
    private readonly Outlet<HttpResponseMessage> _outlet = new("cacheStorage.out");

    public override FlowShape<HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public CacheStorageStage(HttpCacheStore store)
    {
        _store = store;
        Shape = new FlowShape<HttpResponseMessage, HttpResponseMessage>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly CacheStorageStage _stage;

        public Logic(CacheStorageStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var response = Grab(stage._inlet);
                    var request = response.RequestMessage;

                    if (request is not null)
                    {
                        response = Process(request, response);
                    }

                    Push(stage._outlet, response);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet,
                onPull: () => Pull(stage._inlet),
                onDownstreamFinish: _ => CompleteStage());
        }

        private HttpResponseMessage Process(HttpRequestMessage request, HttpResponseMessage response)
        {
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
                    _stage._store.Invalidate(request.RequestUri);
                }

                return response;
            }

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // RFC 9111 §4.3.4 — merge 304 with cached entry and push 200 downstream
                var entry = _stage._store.Get(request);
                if (entry is not null)
                {
                    var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(response, entry);
                    merged.RequestMessage = request;

                    // Update the cache with the refreshed entry
                    var now = DateTimeOffset.UtcNow;
                    _stage._store.Put(request, merged, entry.Body, now, now);

                    return merged;
                }

                return response;
            }

            if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
            {
                // RFC 9111 §3 — store cacheable 2xx responses
                var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                var now = DateTimeOffset.UtcNow;
                _stage._store.Put(request, response, body, now, now);
            }

            return response;
        }
    }
}