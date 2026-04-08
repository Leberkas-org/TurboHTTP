using Akka.Actor;
using Akka.Configuration;
using Akka.Streams;

namespace TurboHTTP.Internal;

/// <summary>
/// Defines dedicated Akka.NET dispatchers for TurboHttp internals.
/// <list type="bullet">
/// <item><see cref="IoDispatcher"/> — ForkJoinDispatcher for <c>ConnectionManagerActor</c> and
/// <c>ClientStreamOwnerActor</c>. Lightweight actor mailbox work isolated from stream processing.</item>
/// <item><see cref="StreamDispatcher"/> — ForkJoinDispatcher for the Akka.Streams materializer.
/// Thread count scales with <see cref="TurboClientOptions.MaxEndpointSubstreams"/>.</item>
/// </list>
/// Both use dedicated thread pools (background threads) so they never contend with the .NET ThreadPool.
/// When an externally provided <see cref="ActorSystem"/> does not include this HOCON,
/// all helpers gracefully fall back to the default dispatcher.
/// </summary>
internal static class TurboHttpDispatchers
{
    public const string IoDispatcher = "akka.actor.turbohttp-io-dispatcher";
    public const string StreamDispatcher = "akka.actor.turbohttp-stream-dispatcher";

    /// <summary>
    /// Applies <see cref="IoDispatcher"/> to <paramref name="props"/> if the
    /// <paramref name="system"/> has it configured; otherwise returns <paramref name="props"/> unchanged.
    /// </summary>
    public static Props WithIoDispatcher(this Props props, ActorSystem system)
        => system.Dispatchers.HasDispatcher(IoDispatcher) ? props.WithDispatcher(IoDispatcher) : props;

    /// <summary>
    /// Applies <see cref="StreamDispatcher"/> to <paramref name="props"/> if the
    /// <paramref name="system"/> has it configured; otherwise returns <paramref name="props"/> unchanged.
    /// </summary>
    public static Props WithStreamDispatcher(this Props props, ActorSystem system)
        => system.Dispatchers.HasDispatcher(StreamDispatcher) ? props.WithDispatcher(StreamDispatcher) : props;

    /// <summary>
    /// Applies <see cref="StreamDispatcher"/> to <paramref name="settings"/> if the
    /// <paramref name="system"/> has it configured; otherwise returns <paramref name="settings"/> unchanged.
    /// </summary>
    public static ActorMaterializerSettings WithStreamDispatcher(
        this ActorMaterializerSettings settings, ActorSystem system)
        => system.Dispatchers.HasDispatcher(StreamDispatcher) ? settings.WithDispatcher(StreamDispatcher) : settings;

    /// <summary>
    /// Builds dispatcher HOCON configuration with thread counts derived from
    /// <paramref name="maxEndpointSubstreams"/>.
    /// <para>
    /// Stream-dispatcher: <c>clamp(maxEndpointSubstreams / 8, ProcessorCount, 64)</c><br/>
    /// IO-dispatcher: <c>clamp(ProcessorCount, 4, 16)</c>
    /// </para>
    /// </summary>
    public static Config CreateConfig(uint maxEndpointSubstreams)
    {
        var streamThreads = Math.Clamp(
            (int)maxEndpointSubstreams / 8,
            Environment.ProcessorCount,
            64);

        var ioThreads = Math.Clamp(
            Environment.ProcessorCount,
            4,
            16);

        return ConfigurationFactory.ParseString($$"""
            akka.actor {
                turbohttp-io-dispatcher {
                    type = ForkJoinDispatcher
                    throughput = 32
                    dedicated-thread-pool {
                        thread-count = {{ioThreads}}
                        threadtype = background
                    }
                }
                turbohttp-stream-dispatcher {
                    type = ForkJoinDispatcher
                    throughput = 64
                    dedicated-thread-pool {
                        thread-count = {{streamThreads}}
                        threadtype = background
                    }
                }
            }
            """);
    }
}
