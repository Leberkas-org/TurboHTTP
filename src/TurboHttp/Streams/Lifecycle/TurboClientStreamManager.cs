using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.Internal;
using Owner = TurboHttp.Streams.Lifecycle.ClientStreamOwner;

namespace TurboHttp.Streams.Lifecycle;

/// <summary>
/// Internal wrapper around <see cref="ClientStreamOwnerActor"/> that maintains backward
/// compatibility with the existing <see cref="TurboHttpClient"/> API.
/// <para>
/// Owns the stable channel endpoints (<see cref="Requests"/> / <see cref="Responses"/>)
/// and delegates stream lifecycle management to the Owner actor.
/// The Owner actor materializes the Akka.Streams pipeline directly using these
/// externally-owned channels.
/// </para>
/// <para>
/// Lifecycle:
/// <list type="bullet">
/// <item>Constructor creates channels and spawns the Owner actor.</item>
/// <item>Owner materializes the stream directly using the channels.</item>
/// <item>On failure, Owner retries with exponential backoff (100ms, 500ms, 2s), reconnecting to the same channels.</item>
/// <item><see cref="Dispose"/> completes the request channel and sends <see cref="ClientStreamOwner.Shutdown"/> to the Owner.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class TurboClientStreamManager : IDisposable
{
    private readonly IActorRef _owner;
    private int _disposed;

    internal ChannelWriter<HttpRequestMessage> Requests { get; }
    internal ChannelReader<HttpResponseMessage> Responses { get; }

    /// <summary>
    /// Exposes the response-channel writer so tests can inject synthetic responses
    /// without requiring a live TCP connection.
    /// </summary>
    internal ChannelWriter<HttpResponseMessage> ResponseWriter { get; }

    internal TurboClientStreamManager(TurboClientOptions clientOptions, Func<TurboRequestOptions> requestOptionsFactory,
        ActorSystem system, PipelineDescriptor descriptor)
    {
        // Create stable channels — these survive instance actor restarts.
        // The manager owns these channels; the instance actor reads/writes but never completes them.
        var requestsChannel = Channel.CreateUnbounded<HttpRequestMessage>(new UnboundedChannelOptions
        {
            SingleReader = true
        });
        var responsesChannel = Channel.CreateUnbounded<HttpResponseMessage>(new UnboundedChannelOptions
        {
            SingleWriter = true
        });

        Requests = requestsChannel.Writer;
        Responses = responsesChannel.Reader;
        ResponseWriter = responsesChannel.Writer;

        // Create the Owner actor — it materializes the stream directly,
        // tracks pending work, and handles retry with exponential backoff.
        // Uses dedicated dispatcher if available; falls back to default for external ActorSystems.
        _owner = system.ActorOf(
            Props.Create(() => new ClientStreamOwnerActor())
                .WithStreamDispatcher(system),
            $"stream-owner-{Guid.NewGuid():N}");

        // Tell the Owner to create a stream instance. The instance will materialize
        // the Akka.Streams pipeline using our channels. Requests written to the channel
        // before materialization completes are buffered in the unbounded channel.
        _owner.Tell(new Owner.CreateStreamInstance(
            clientOptions,
            requestOptionsFactory,
            descriptor,
            requestsChannel.Reader,
            responsesChannel.Writer));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Complete the request channel — the source will finish, the pipeline
        // drains, and the instance actor stops writing to the response channel.
        Requests.TryComplete();

        // Signal the Owner to shut down gracefully. It waits for pending work
        // to drain (up to 5s), then stops the instance and itself.
        _owner.Tell(new Owner.Shutdown());

        // Complete the response channel so downstream ITurboHttpClient.Responses consumers terminate.
        ResponseWriter.TryComplete();
    }

    /// <summary>
    /// Returns a <see cref="Task"/> that completes when the owner actor has fully stopped.
    /// Sends a <see cref="Owner.Shutdown"/> message (harmlessly ignored if already shutting down)
    /// and waits for actor termination up to <paramref name="timeout"/>.
    /// </summary>
    internal Task WhenTerminatedAsync(TimeSpan timeout)
        => _owner.GracefulStop(timeout, new Owner.Shutdown());
}