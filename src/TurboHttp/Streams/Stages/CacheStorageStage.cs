using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC9111;

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
///     The body is read via a synchronous fast-path when content is already buffered in memory
///     (e.g. <see cref="ByteArrayContent"/>), or asynchronously via <see cref="GraphStageLogic.GetAsyncCallback{T}"/>
///     for other content types.
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

    private readonly Inlet<HttpResponseMessage> _in = new("CacheStorage.In");
    private readonly Outlet<HttpResponseMessage> _out = new("CacheStorage.Out");

    public override FlowShape<HttpResponseMessage, HttpResponseMessage> Shape { get; }


    public CacheStorageStage(HttpCacheStore store)
    {
        _store = store;
        Shape = new FlowShape<HttpResponseMessage, HttpResponseMessage>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly CacheStorageStage _stage;
        private Action<(HttpResponseMessage response, byte[] body)>? _onBodyRead;

        public Logic(CacheStorageStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var response = Grab(stage._in);
                    var request = response.RequestMessage;

                    if (request is not null)
                    {
                        var (result, needsAsyncRead) = Process(request, response);
                        if (!needsAsyncRead)
                        {
                            Push(stage._out, result);
                        }
                        // When needsAsyncRead is true, the async callback will push downstream.
                    }
                    else
                    {
                        Push(stage._out, response);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("CacheStorageStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart()
        {
            _onBodyRead = GetAsyncCallback<(HttpResponseMessage response, byte[] body)>(tuple =>
            {
                var (response, body) = tuple;
                var now = DateTimeOffset.UtcNow;
                _stage._store.Put(response.RequestMessage!, response, body, now, now);
                Push(_stage._out, response);
            });
        }

        /// <summary>
        /// Processes a response for caching. Returns the response to push and whether an async
        /// body read was initiated. When <c>needsAsyncRead</c> is true, the caller must NOT push
        /// — the async callback will push after the body has been read.
        /// </summary>
        private (HttpResponseMessage response, bool needsAsyncRead) Process(
            HttpRequestMessage request, HttpResponseMessage response)
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

                return (response, false);
            }

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // RFC 9111 §4.3.4 — merge 304 with cached entry and push 200 downstream
                var entry = _stage._store.Get(request);
                if (entry is not null)
                {
                    var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(response, entry);
                    merged.RequestMessage = request;

                    var now = DateTimeOffset.UtcNow;
                    _stage._store.Put(request, merged, entry.Body, now, now);

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
                    _stage._store.Put(request, response, body, now, now);
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
    }
}
