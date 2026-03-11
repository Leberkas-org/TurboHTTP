using System;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// RFC 9111 §4 — Checks the in-memory cache before forwarding requests to the network.
/// <para>
/// On each incoming <see cref="HttpRequestMessage"/>:
/// <list type="bullet">
///   <item><description>
///     <b>Fresh / Stale (max-stale allowed)</b> — the cached <see cref="HttpResponseMessage"/> is
///     emitted directly on <see cref="FanOutShape{TIn,TOut0,TOut1}.Out1"/> (hit outlet).
///   </description></item>
///   <item><description>
///     <b>MustRevalidate</b> — a conditional request (If-None-Match / If-Modified-Since) is built
///     via <see cref="CacheValidationRequestBuilder"/> and forwarded on
///     <see cref="FanOutShape{TIn,TOut0,TOut1}.Out0"/> (miss outlet).
///   </description></item>
///   <item><description>
///     <b>Miss</b> — the original request is forwarded unchanged on
///     <see cref="FanOutShape{TIn,TOut0,TOut1}.Out0"/> (miss outlet).
///   </description></item>
/// </list>
/// </para>
/// Both downstream outlets must have demand before the stage pulls the inlet.
/// This guarantees that regardless of the routing decision, there is always a consumer ready.
/// </summary>
internal sealed class CacheLookupStage
    : GraphStage<FanOutShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage>>
{
    private readonly HttpCacheStore _store;
    private readonly CachePolicy _policy;

    private readonly Inlet<HttpRequestMessage> _in
        = new("cacheLookup.in");

    private readonly Outlet<HttpRequestMessage> _outMiss
        = new("cacheLookup.out0.miss");

    private readonly Outlet<HttpResponseMessage> _outHit
        = new("cacheLookup.out1.hit");

    public override FanOutShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage> Shape { get; }

    public CacheLookupStage(HttpCacheStore store, CachePolicy? policy = null)
    {
        _store = store;
        _policy = policy ?? CachePolicy.Default;
        Shape = new FanOutShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage>(
            _in, _outMiss, _outHit);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly CacheLookupStage _stage;
        private bool _missHasDemand;
        private bool _hitHasDemand;

        public Logic(CacheLookupStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.Shape.In,
                onPush: () =>
                {
                    var request = Grab(stage.Shape.In);
                    var entry = _stage._store.Get(request);
                    var result =
                        CacheFreshnessEvaluator.Evaluate(entry, request, DateTimeOffset.UtcNow, _stage._policy);

                    if (result.Status is CacheLookupStatus.Fresh or CacheLookupStatus.Stale)
                    {
                        _hitHasDemand = false;
                        Push(stage.Shape.Out1, result.Entry!.Response);
                    }
                    else
                    {
                        _missHasDemand = false;
                        var outgoing = result is { Status: CacheLookupStatus.MustRevalidate, Entry: not null }
                            ? CacheValidationRequestBuilder.BuildConditionalRequest(request, result.Entry)
                            : request;
                        Push(stage.Shape.Out0, outgoing);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Shape.Out0,
                onPull: () =>
                {
                    _missHasDemand = true;
                    TryPullInlet();
                },
                onDownstreamFinish: _ => CompleteStage());

            SetHandler(stage.Shape.Out1,
                onPull: () =>
                {
                    _hitHasDemand = true;
                    TryPullInlet();
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        private void TryPullInlet()
        {
            if (_missHasDemand && _hitHasDemand && !HasBeenPulled(_stage.Shape.In))
            {
                Pull(_stage.Shape.In);
            }
        }
    }
}