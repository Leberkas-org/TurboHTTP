using Akka;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using TurboHttp.Internal;

namespace TurboHttp.Streams.Stages.Internal;

/// <summary>
/// Implements Akka's public <see cref="IMergeBack{TIn,TMat}"/> interface so that
/// <see cref="SubFlowImpl{TIn,TOut,TMat,TClosed}"/> can drive our custom
/// host-key grouping/merging stages.
/// </summary>
internal sealed class HostKeyMergeBack<TIn, TMat> : IMergeBack<TIn, TMat>
{
    private readonly IFlow<TIn, TMat> _baseFlow;
    private readonly Func<TIn, RequestEndpoint> _keyFunction;
    private readonly int _maxSubstreams;
    private readonly int _queueSize;

    public HostKeyMergeBack(IFlow<TIn, TMat> baseFlow, Func<TIn, RequestEndpoint> keyFunction, int maxSubstreams,
        int queueSize = 64)
    {
        _baseFlow = baseFlow;
        _keyFunction = keyFunction;
        _maxSubstreams = maxSubstreams;
        _queueSize = queueSize;
    }

    // Called by SubFlowImpl.MergeSubstreamsWithParallelism(breadth).
    // `flow` is the accumulated per-substream Flow built up via
    // SubFlowImpl.Via() calls (starts as identity, grows with each operator).
    public IFlow<TOut, TMat> Apply<TOut>(Flow<TIn, TOut, TMat> flow, int breadth)
    {
        var effectiveBreadth = breadth is <= 0 or int.MaxValue
            ? _maxSubstreams
            : breadth;

        return _baseFlow
            .Via(new GroupByRequestKeyStage<TIn>(_keyFunction, _maxSubstreams, _queueSize))
            .Via(Flow.Create<Source<TIn, NotUsed>>()
                .Select(src => src.Via(flow)))
            .Via(new MergeSubstreamsStage<TOut>(effectiveBreadth));
    }
}