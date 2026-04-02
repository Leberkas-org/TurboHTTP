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
    private readonly uint _maxSubstreams;
    private readonly Func<RequestEndpoint, int>? _maxSubstreamsPerKey;
    private readonly Func<RequestEndpoint, int>? _maxConcurrencyPerSlot;

    public HostKeyMergeBack(IFlow<TIn, TMat> baseFlow, Func<TIn, RequestEndpoint> keyFunction, uint maxSubstreams,
        Func<RequestEndpoint, int>? maxSubstreamsPerKey = null,
        Func<RequestEndpoint, int>? maxConcurrencyPerSlot = null)
    {
        _baseFlow = baseFlow;
        _keyFunction = keyFunction;
        _maxSubstreams = maxSubstreams;
        _maxSubstreamsPerKey = maxSubstreamsPerKey;
        _maxConcurrencyPerSlot = maxConcurrencyPerSlot;
    }

    // Called by SubFlowImpl.MergeSubstreamsWithParallelism(breadth).
    // `flow` is the accumulated per-substream Flow built up via
    // SubFlowImpl.Via() calls (starts as identity, grows with each operator).
    public IFlow<TOut, TMat> Apply<TOut>(Flow<TIn, TOut, TMat> flow, int breadth)
    {
        var maxSubstreams = Convert.ToInt32(_maxSubstreams);
        var effectiveBreadth = breadth is <= 0 or int.MaxValue
            ? maxSubstreams
            : breadth;

        return _baseFlow
            .Via(new GroupByRequestEndpointStage<TIn>(_keyFunction, maxSubstreams, _maxSubstreamsPerKey, _maxConcurrencyPerSlot))
            .Via(Flow.Create<Source<TIn, NotUsed>>()
                .Select(src => src.Via(flow)))
            .Via(new MergeSubstreamsStage<TOut>(effectiveBreadth));
    }
}