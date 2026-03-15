using System;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using TurboHttp.IO.Stages;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// Implements Akka's public <see cref="IMergeBack{TIn,TMat}"/> interface so that
/// <see cref="SubFlowImpl{TIn,TOut,TMat,TClosed}"/> can drive our custom
/// host-key grouping/merging stages.
/// </summary>
internal sealed class HostKeyMergeBack<TIn, TMat> : IMergeBack<TIn, TMat>
{
    private readonly IFlow<TIn, TMat> _baseFlow;
    private readonly Func<TIn, HostKey> _keyFunction;
    private readonly int _maxSubstreams;

    public HostKeyMergeBack(IFlow<TIn, TMat> baseFlow, Func<TIn, HostKey> keyFunction, int maxSubstreams)
    {
        _baseFlow = baseFlow;
        _keyFunction = keyFunction;
        _maxSubstreams = maxSubstreams;
    }

    // Called by SubFlowImpl.MergeSubstreamsWithParallelism(breadth).
    // `innerFlow` is the accumulated per-substream Flow built up via
    // SubFlowImpl.Via() calls (starts as identity, grows with each operator).
    public IFlow<TOut, TMat> Apply<TOut>(Flow<TIn, TOut, TMat> innerFlow, int breadth)
    {
        var effectiveBreadth = breadth is <= 0 or int.MaxValue
            ? _maxSubstreams
            : breadth;

        return _baseFlow
            .Via(new GroupByHostKeyStage<TIn>(_keyFunction, _maxSubstreams))
            .Via(Flow.Create<Source<TIn, NotUsed>>()
                .Select(src => src.Via(innerFlow)))
            .Via(new MergeSubstreamsStage<TOut>(effectiveBreadth));
    }
}

public static class FlowHostKeyGroupByExtensions
{
    /// <summary>
    /// Groups elements by <see cref="HostKey"/> and returns a real Akka
    /// <see cref="SubFlow{TOut,TMat,TClosed}"/> so that all standard
    /// <c>SubFlowOperations</c> methods (Select, Where, Take, Via, …)
    /// apply directly without any custom wrapper type.
    /// Close the subflow with <c>.MergeSubstreams()</c>.
    /// </summary>
    public static SubFlow<T, TMat, Sink<T, TMat>> GroupBy<T, TMat>(
        this IFlow<T, TMat> flow,
        Func<T, HostKey> keyFunction,
        int maxSubstreams)
    {
        var mergeBack = new HostKeyMergeBack<T, TMat>(flow, keyFunction, maxSubstreams);

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
