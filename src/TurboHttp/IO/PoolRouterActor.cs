using System;
using System.Collections.Generic;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class PoolRouterActor : ReceiveActor
{
    // ── Public message protocol ───────────────────────────────────────

    /// <summary>
    /// Sent by ConnectionStage once on startup to obtain the global request/response handles.
    /// The actor replies with <see cref="GlobalRefs"/> to the sender.
    /// </summary>
    public sealed record GetGlobalRefs;

    /// <summary>Global stream handles returned to ConnectionStage.</summary>
    public sealed record GlobalRefs(
        ISourceQueueWithComplete<DataItem> RequestQueue,
        Source<DataItem, NotUsed> ResponseSource);

    /// <summary>
    /// Sent by ConnectionStage on each ConnectItem to ensure a HostPoolActor exists.
    /// Fire-and-forget — no reply.
    /// </summary>
    public sealed record EnsureHost(HostKey Key, TcpOptions Options);

    /// <summary>
    /// Sent by HostPoolActor in PreStart to wire its aggregated response source into the global MergeHub.
    /// </summary>
    public sealed record RegisterHostResponseSource(Source<DataItem, NotUsed> ResponseSource);

    // ── Fields ────────────────────────────────────────────────────────

    private readonly PoolConfig _config;
    private readonly Func<TcpOptions, PoolConfig, HostKey, IActorRef> _hostFactory;
    private readonly Dictionary<HostKey, IActorRef> _hosts = new();

    private IMaterializer? _mat;
    private Sink<DataItem, NotUsed>? _globalMergeHubSink;
    private ISourceQueueWithComplete<DataItem>? _globalRequestQueue;
    private Source<DataItem, NotUsed>? _globalResponseSource;

    public PoolRouterActor(PoolConfig? config = null,
        Func<TcpOptions, PoolConfig, HostKey, IActorRef>? hostFactory = null)
    {
        _config = config ?? new PoolConfig();
        _hostFactory = hostFactory ?? CreateHostPoolActor;

        Receive<GetGlobalRefs>(HandleGetGlobalRefs);
        Receive<EnsureHost>(HandleEnsureHost);
        Receive<RegisterHostResponseSource>(HandleRegisterHostResponseSource);
        Receive<DataItem>(HandleDataItem);
    }

    protected override void PreStart()
    {
        _mat = Context.System.Materializer();

        var self = Self;

        // Global request intake: ConnectionStage writes stamped DataItems here;
        // items are routed through the actor mailbox for thread-safe key→host dispatch.
        var (requestQueue, requestSource) =
            Source.Queue<DataItem>(256, OverflowStrategy.Backpressure)
                .PreMaterialize(_mat);

        _globalRequestQueue = requestQueue;
        requestSource.RunWith(Sink.ForEach<DataItem>(item => self.Tell(item)), _mat);

        // Global response aggregation: all HostPoolActors wire their response sources here.
        var (mergeHubSink, responseSource) = MergeHub.Source<DataItem>().PreMaterialize(_mat);
        _globalMergeHubSink = mergeHubSink;
        _globalResponseSource = responseSource;
    }

    // ── Message handlers ──────────────────────────────────────────────

    private void HandleGetGlobalRefs(GetGlobalRefs _)
    {
        Sender.Tell(new GlobalRefs(_globalRequestQueue!, _globalResponseSource!));
    }

    private void HandleEnsureHost(EnsureHost msg)
    {
        EnsureHostActor(msg.Key, msg.Options);
    }

    private void HandleRegisterHostResponseSource(RegisterHostResponseSource msg)
    {
        msg.ResponseSource.RunWith(_globalMergeHubSink!, _mat!);
    }

    private void HandleDataItem(DataItem item)
    {
        if (_hosts.TryGetValue(item.Key, out var hostActor))
        {
            hostActor.Tell(item);
        }
        // DataItem for unknown host is dropped — EnsureHost must precede DataItems.
    }

    private IActorRef EnsureHostActor(HostKey key, TcpOptions options)
    {
        if (!_hosts.TryGetValue(key, out var hostActor))
        {
            hostActor = _hostFactory(options, _config, key);
            _hosts[key] = hostActor;
        }

        return hostActor;
    }

    private IActorRef CreateHostPoolActor(TcpOptions options, PoolConfig config, HostKey key)
    {
        var hostConfig = new HostPoolActor.HostPoolConfig(options, config, key);
        return Context.ActorOf(Props.Create(() => new HostPoolActor(hostConfig)), Guid.NewGuid().ToString());
    }
}
