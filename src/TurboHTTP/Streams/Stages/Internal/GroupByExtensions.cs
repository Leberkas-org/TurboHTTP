using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using TurboHTTP.Internal;

namespace TurboHTTP.Streams.Stages.Internal;

internal static class GroupByExtensions
{
    /// <summary>
    /// Groups elements by <see cref="RequestEndpoint"/> and returns a real Akka
    /// <see cref="SubFlow{TOut,TMat,TClosed}"/> so that all standard
    /// <c>SubFlowOperations</c> methods (Select, Where, Take, Via, …)
    /// apply directly without any custom wrapper type.
    /// Close the subflow with <c>.MergeSubstreams()</c>.
    /// </summary>
    public static SubFlow<T, TMat, Sink<T, TMat>> GroupByRequestEndpoint<T, TMat>(
        this IFlow<T, TMat> flow,
        Func<T, RequestEndpoint> keyFunction,
        uint maxSubstreams,
        Func<RequestEndpoint, int>? maxSubstreamsPerKey = null,
        Func<RequestEndpoint, int>? maxConcurrencyPerSlot = null)
    {
        var mergeBack = new HostKeyMergeBack<T, TMat>(flow, keyFunction, maxSubstreams, maxSubstreamsPerKey, maxConcurrencyPerSlot);

        // Flow.Create<T>() gives Flow<T,T,NotUsed>; cast is safe because callers always
        // start with a flow whose TMat is NotUsed (e.g. Flow.Create<HttpRequestMessage>()).
        return new SubFlowImpl<T, T, TMat, Sink<T, TMat>>(
            Flow.Create<T, TMat>(),
            mergeBack,
            s => s);
    }

    /// <summary>
    /// Attaches <paramref name="flow"/> to each substream and returns a typed
    /// <see cref="SubFlow{TOut2,TMat,TClosed}"/>, preserving <c>TClosed</c>.
    ///
    /// In Akka.NET, <c>SubFlow.Via()</c> is an instance method that returns
    /// <c>IFlow&lt;TOut2, TMat&gt;</c> — extension methods cannot shadow it.
    /// This helper provides the typed variant under a distinct name so callers
    /// can chain further SubFlow operators or call <c>.MergeSubstreams()</c>
    /// without an explicit cast at every call site.
    /// </summary>
    public static SubFlow<TOut2, TMat, TClosed> ViaSubFlow<TOut, TOut2, TMat, TClosed>(
        this SubFlow<TOut, TMat, TClosed> subFlow,
        IGraph<FlowShape<TOut, TOut2>, NotUsed> flow)
    {
        // SubFlow.Via() creates a new SubFlowImpl internally and returns it as
        // IFlow<TOut2, TMat>; the cast back to SubFlow is always safe.
        return (SubFlow<TOut2, TMat, TClosed>)subFlow.Via(flow);
    }
}
