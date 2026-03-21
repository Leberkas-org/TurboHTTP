using System.Collections.Immutable;
using System.Net.Http;
using Akka.Streams;

namespace TurboHttp.Streams;

/// <summary>
/// Shape for the post-processing sub-graph: 2 inlets (response, cache hits),
/// 3 outlets (final response, retry feedback, redirect feedback).
/// </summary>
internal sealed class PostProcessShape : Shape
{
    public Inlet<HttpResponseMessage> ResponseIn { get; }
    public Inlet<HttpResponseMessage> CacheHitIn { get; }
    public Outlet<HttpResponseMessage> ResponseOut { get; }
    public Outlet<HttpRequestMessage> RetryFeedbackOut { get; }
    public Outlet<HttpRequestMessage> RedirectFeedbackOut { get; }

    public PostProcessShape(
        Inlet<HttpResponseMessage> responseIn,
        Inlet<HttpResponseMessage> cacheHitIn,
        Outlet<HttpResponseMessage> responseOut,
        Outlet<HttpRequestMessage> retryFeedbackOut,
        Outlet<HttpRequestMessage> redirectFeedbackOut)
    {
        ResponseIn = responseIn;
        CacheHitIn = cacheHitIn;
        ResponseOut = responseOut;
        RetryFeedbackOut = retryFeedbackOut;
        RedirectFeedbackOut = redirectFeedbackOut;
    }

    public override ImmutableArray<Inlet> Inlets =>
        [ResponseIn, CacheHitIn];

    public override ImmutableArray<Outlet> Outlets =>
        [ResponseOut, RetryFeedbackOut, RedirectFeedbackOut];

    public override Shape DeepCopy() => new PostProcessShape(
        (Inlet<HttpResponseMessage>)ResponseIn.CarbonCopy(),
        (Inlet<HttpResponseMessage>)CacheHitIn.CarbonCopy(),
        (Outlet<HttpResponseMessage>)ResponseOut.CarbonCopy(),
        (Outlet<HttpRequestMessage>)RetryFeedbackOut.CarbonCopy(),
        (Outlet<HttpRequestMessage>)RedirectFeedbackOut.CarbonCopy());

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        => new PostProcessShape(
            (Inlet<HttpResponseMessage>)inlets[0],
            (Inlet<HttpResponseMessage>)inlets[1],
            (Outlet<HttpResponseMessage>)outlets[0],
            (Outlet<HttpRequestMessage>)outlets[1],
            (Outlet<HttpRequestMessage>)outlets[2]);
}
