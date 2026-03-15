using System;
using System.Collections.Generic;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class PoolRouterActor : ReceiveActor
{
    // ── Private async-handshake messages ─────────────────────────────

    private sealed record SinkRefReady(ISinkRef<IOutputItem> SinkRef);

    private sealed record SinkRefFailed(Exception Error);

    private sealed record SourceRefReady(ISourceRef<DataItem> SourceRef);

    private sealed record SourceRefFailed(Exception Error);

    // ── Public message protocol ───────────────────────────────────────

    public sealed record GetPoolRefs;

    public sealed record PoolRefs(ISinkRef<IOutputItem> Sink, ISourceRef<DataItem> Source);

    // ── Fields ────────────────────────────────────────────────────────

    private readonly PoolConfig _config;
    private readonly Func<TcpOptions, PoolConfig, HostKey, IActorRef> _hostFactory;
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly Dictionary<HostKey, IActorRef> _hosts = new();
    private readonly List<IActorRef> _pendingReplies = [];

    private IMaterializer? _mat;
    private Sink<DataItem, NotUsed>? _mergeHubSink;
    private ISinkRef<IOutputItem>? _sinkRef;
    private ISourceRef<DataItem>? _sourceRef;

    public PoolRouterActor(PoolConfig? config = null,
        Func<TcpOptions, PoolConfig, HostKey, IActorRef>? hostFactory = null)
    {
        _config = config ?? new PoolConfig();
        _hostFactory = hostFactory ?? CreateHostPoolActor;

        Receive<ISignalItem>(RouteSignalItem);
        Receive<IOutputItem>(RouteOutputItem);
        Receive<HostPoolActor.HostStreamRefsReady>(HandleHostStreamRefsReady);
        Receive<GetPoolRefs>(HandleGetPoolRefs);

        Receive<SinkRefReady>(msg =>
        {
            _sinkRef = msg.SinkRef;
            TrySendPendingReplies();
        });

        Receive<SinkRefFailed>(msg => _log.Error(msg.Error, "SinkRef initialization failed"));

        Receive<SourceRefReady>(msg =>
        {
            _sourceRef = msg.SourceRef;
            TrySendPendingReplies();
        });

        Receive<SourceRefFailed>(msg => _log.Error(msg.Error, "SourceRef initialization failed"));
    }

    protected override void PreStart()
    {
        _mat = Context.System.Materializer();

        var (mergeHubSink, mergeHubSource) = MergeHub.Source<DataItem>().PreMaterialize(_mat);
        _mergeHubSink = mergeHubSink;

        // Materialize the merged SourceRef for all host responses
        mergeHubSource
            .RunWith(StreamRefs.SourceRef<DataItem>(), _mat)
            .PipeTo(
                Self,
                success: sourceRef => new SourceRefReady(sourceRef),
                failure: ex => new SourceRefFailed(ex));

        // Materialize the routing SinkRef — items are forwarded to Self for actor-thread routing
        var self = Self;
        Sink.ForEach<IOutputItem>(item => self.Tell(item))
            .RunWith(StreamRefs.SinkRef<IOutputItem>(), _mat)
            .PipeTo(
                Self,
                success: sinkRef => new SinkRefReady(sinkRef),
                failure: ex => new SinkRefFailed(ex));
    }

    // ── Item routing ──────────────────────────────────────────────────

    private void RouteSignalItem(ISignalItem item)
    {
        var key = item.Key;
        if (!_hosts.TryGetValue(key, out var hostActor) && item is ConnectItem connectItem)
        {
            hostActor = _hostFactory(connectItem.Options, _config, key);
            _hosts[key] = hostActor;
        }

        hostActor.Forward(item);
    }

    private void RouteOutputItem(IOutputItem item)
    {
        var key = item.Key;
        if (_hosts.TryGetValue(key, out var hostActor))
        {
            hostActor.Forward(item);
        }
        else
        {
            _log.Warning("No HostPoolActor registered for key {0}, dropping item", key.Key);
        }
    }

    private void HandleHostStreamRefsReady(HostPoolActor.HostStreamRefsReady msg)
    {
        msg.Source.Source.RunWith(_mergeHubSink!, _mat!);
    }

    // ── GetPoolRefs handler ───────────────────────────────────────────

    private void HandleGetPoolRefs(GetPoolRefs _)
    {
        if (_sinkRef is not null && _sourceRef is not null)
        {
            Sender.Tell(new PoolRefs(_sinkRef, _sourceRef));
            return;
        }

        _pendingReplies.Add(Sender);
    }

    private void TrySendPendingReplies()
    {
        if (_sinkRef == null || _sourceRef == null)
        {
            return;
        }

        foreach (var pending in _pendingReplies)
        {
            pending.Tell(new PoolRefs(_sinkRef, _sourceRef));
        }

        _pendingReplies.Clear();
    }

    private IActorRef CreateHostPoolActor(TcpOptions options, PoolConfig config, HostKey key)
    {
        var hostConfig = new HostPoolActor.HostPoolConfig(options, config, key);
        return Context.ActorOf(Props.Create(() => new HostPoolActor(hostConfig)), Guid.NewGuid().ToString());
    }
}