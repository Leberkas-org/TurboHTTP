using System;
using Akka;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using TurboHttp.IO.Stages;

namespace TurboHttp.Internal;

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