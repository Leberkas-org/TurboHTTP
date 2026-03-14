using System;
using System.Collections.Generic;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class PoolRouterActor : ReceiveActor
{
    // ── Private async-handshake messages ─────────────────────────────

    private sealed record SinkRefReady(ISinkRef<ITransportItem> SinkRef);

    private sealed record SinkRefFailed(Exception Error);

    private sealed record SourceRefReady(ISourceRef<IDataItem> SourceRef);

    private sealed record SourceRefFailed(Exception Error);

    // ── Public message protocol ───────────────────────────────────────

    public sealed record GetPoolRefs;

    public sealed record PoolRefs(ISinkRef<ITransportItem> Sink, ISourceRef<IDataItem> Source);

    // ── Fields ────────────────────────────────────────────────────────

    private readonly PoolConfig _config;
    private readonly Func<TcpOptions, PoolConfig, IActorRef> _hostFactory;
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly Dictionary<HostKey, IActorRef> _hosts = new();
    private readonly List<IActorRef> _pendingReplies = new();

    private IMaterializer? _mat;
    private Sink<IDataItem, NotUsed>? _mergeHubSink;
    private ISinkRef<ITransportItem>? _sinkRef;
    private ISourceRef<IDataItem>? _sourceRef;

    public PoolRouterActor(PoolConfig? config = null, Func<TcpOptions, PoolConfig, IActorRef>? hostFactory = null)
    {
        _config = config ?? new PoolConfig();
        _hostFactory = hostFactory ?? CreateHostPoolActor;

        Receive<ITransportItem>(RouteItem);
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

        var (mergeHubSink, mergeHubSource) = MergeHub.Source<IDataItem>().PreMaterialize(_mat);
        _mergeHubSink = mergeHubSink;

        // Materialize the merged SourceRef for all host responses
        mergeHubSource
            .RunWith(StreamRefs.SourceRef<IDataItem>(), _mat)
            .PipeTo(
                Self,
                success: sourceRef => new SourceRefReady(sourceRef),
                failure: ex => new SourceRefFailed(ex));

        // Materialize the routing SinkRef — items are forwarded to Self for actor-thread routing
        var self = Self;
        Sink.ForEach<ITransportItem>(item => self.Tell(item))
            .RunWith(StreamRefs.SinkRef<ITransportItem>(), _mat)
            .PipeTo(
                Self,
                success: sinkRef => new SinkRefReady(sinkRef),
                failure: ex => new SinkRefFailed(ex));
    }

    // ── Item routing ──────────────────────────────────────────────────

    private void RouteItem(ITransportItem item)
    {
        HostKey key;

        if (item is ConnectItem connect)
        {
            key = new HostKey
            {
                Schema = "http",
                Host = connect.Options.Host,
                Port = (ushort)connect.Options.Port
            };
        }
        else
        {
            key = item.Key;

            if (key.Equals(HostKey.Default))
            {
                _log.Warning("Received DataItem with no HostKey, dropping");
                return;
            }
        }

        if (!_hosts.TryGetValue(key, out var hostActor))
        {
            if (item is not ConnectItem connectItem)
            {
                _log.Warning("Received item for unknown host {0}, dropping", key);
                return;
            }

            hostActor = _hostFactory(connectItem.Options, _config);
            _hosts[key] = hostActor;
        }

        hostActor.Forward(item);
    }

    private void HandleHostStreamRefsReady(HostPoolActor.HostStreamRefsReady msg)
    {
        msg.Source.Source.RunWith(_mergeHubSink!, _mat!);
    }

    // ── GetPoolRefs handler ───────────────────────────────────────────

    private void HandleGetPoolRefs(GetPoolRefs _)
    {
        if (_sinkRef != null && _sourceRef != null)
        {
            Sender.Tell(new PoolRefs(_sinkRef, _sourceRef));
        }
        else
        {
            _pendingReplies.Add(Sender);
        }
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

    private IActorRef CreateHostPoolActor(TcpOptions options, PoolConfig config)
        => Context.ActorOf(Props.Create(() => new HostPoolActor(options, config)));
}
