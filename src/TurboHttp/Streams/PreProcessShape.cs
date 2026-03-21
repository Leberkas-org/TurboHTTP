using System.Collections.Immutable;
using System.Net.Http;
using Akka.Streams;

namespace TurboHttp.Streams;

/// <summary>
/// Shape for the pre-processing sub-graph: 3 inlets (source requests, redirect feedback, retry feedback),
/// 2 outlets (cache miss → engine, cache hit → post-processing).
/// </summary>
internal sealed class PreProcessShape : Shape
{
    public Inlet<HttpRequestMessage> RequestIn { get; }
    public Inlet<HttpRequestMessage> RedirectFeedbackIn { get; }
    public Inlet<HttpRequestMessage> RetryFeedbackIn { get; }
    public Outlet<HttpRequestMessage> CacheMissOut { get; }
    public Outlet<HttpResponseMessage> CacheHitOut { get; }

    public PreProcessShape(
        Inlet<HttpRequestMessage> requestIn,
        Inlet<HttpRequestMessage> redirectFeedbackIn,
        Inlet<HttpRequestMessage> retryFeedbackIn,
        Outlet<HttpRequestMessage> cacheMissOut,
        Outlet<HttpResponseMessage> cacheHitOut)
    {
        RequestIn = requestIn;
        RedirectFeedbackIn = redirectFeedbackIn;
        RetryFeedbackIn = retryFeedbackIn;
        CacheMissOut = cacheMissOut;
        CacheHitOut = cacheHitOut;
    }

    public override ImmutableArray<Inlet> Inlets =>
        [RequestIn, RedirectFeedbackIn, RetryFeedbackIn];

    public override ImmutableArray<Outlet> Outlets =>
        [CacheMissOut, CacheHitOut];

    public override Shape DeepCopy() => new PreProcessShape(
        (Inlet<HttpRequestMessage>)RequestIn.CarbonCopy(),
        (Inlet<HttpRequestMessage>)RedirectFeedbackIn.CarbonCopy(),
        (Inlet<HttpRequestMessage>)RetryFeedbackIn.CarbonCopy(),
        (Outlet<HttpRequestMessage>)CacheMissOut.CarbonCopy(),
        (Outlet<HttpResponseMessage>)CacheHitOut.CarbonCopy());

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        => new PreProcessShape(
            (Inlet<HttpRequestMessage>)inlets[0],
            (Inlet<HttpRequestMessage>)inlets[1],
            (Inlet<HttpRequestMessage>)inlets[2],
            (Outlet<HttpRequestMessage>)outlets[0],
            (Outlet<HttpResponseMessage>)outlets[1]);
}
