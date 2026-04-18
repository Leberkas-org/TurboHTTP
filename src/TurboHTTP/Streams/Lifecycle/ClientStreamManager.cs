using System.Threading.Channels;
using Akka.Actor;

namespace TurboHTTP.Streams.Lifecycle;

// Message Flow Diagrams (Merged Design: Owner handles materialization directly)
//
// HAPPY PATH: Create → Materialize → Run → Shutdown
//
//   StreamManager                   Owner
//       │                             │
//       │──CreateStreamInstance──────▶│
//       │                             │──materialize pipeline (inline)
//       │◀─StreamInstanceCreated──────│  (success)
//       │                             │
//       │   ... requests flow through channels ...
//       │                             │   (sink completes when channels close)
//       │                             │
//       │──Shutdown──────────────────▶│
//       │                             │──kill stream via KillSwitch
//       │                             │──cleanup resources (materializer, pool)
//       │◀───(actor terminated)───────│
//
//
// ERROR PATH: Materialization Failure → Retry with Backoff
//
//   StreamManager                   Owner
//       │                             │
//       │──CreateStreamInstance──────▶│
//       │                             │──materialize pipeline (inline)
//       │                             │  └─ throws exception
//       │                             │──CleanupForRetry() [explicit cleanup]
//       │                             │
//       │                             │ (retry attempt 1, backoff 100ms)
//       │                             │──materialize pipeline (inline)
//       │                             │  └─ throws exception again
//       │                             │──CleanupForRetry()
//       │                             │
//       │                             │ (retry attempt 2, backoff 500ms)
//       │                             │──materialize pipeline (inline)
//       │◀─StreamInstanceCreated──────│ (success!)
//       │                             │
//
//
// ERROR PATH: Retries Exhausted
//
//   StreamManager                   Owner
//       │                             │
//       │──CreateStreamInstance──────▶│
//       │                             │ ... 3 failed attempts (100ms, 500ms, 2s) ...
//       │◀─StreamInstanceFailed───────│ (retries exhausted)
//       │  (propagate error)          │
//       │                             ╳

/// <summary>
/// Internal wrapper around <see cref="ClientStreamOwner"/> that maintains backward
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
internal sealed class ClientStreamManager : IDisposable
{
    private readonly IActorRef _owner;
    private int _disposed;

    private readonly Channel<HttpRequestMessage> _requests = Channel.CreateUnbounded<HttpRequestMessage>(
        new UnboundedChannelOptions
        {
            SingleReader = true
        });

    private readonly Channel<HttpResponseMessage> _responses = Channel.CreateUnbounded<HttpResponseMessage>(
        new UnboundedChannelOptions
        {
            SingleWriter = true
        });

    internal ChannelWriter<HttpRequestMessage> Requests => _requests.Writer;
    internal ChannelReader<HttpResponseMessage> Responses => _responses.Reader;

    internal ClientStreamManager(TurboClientOptions clientOptions, Func<TurboRequestOptions> requestOptionsFactory,
        ActorSystem system, PipelineDescriptor descriptor)
    {
        // Create the Owner actor — it materializes the stream directly,
        // tracks pending work, and handles retry with exponential backoff.
        // Uses dedicated dispatcher if available; falls back to default for external ActorSystems.
        _owner = system.ActorOf(
            Props.Create(() => new ClientStreamOwner()),
            $"stream-owner-{Guid.NewGuid():N}");

        // Tell the Owner to create a stream instance. The instance will materialize
        // the Akka.Streams pipeline using our channels. Requests written to the channel
        // before materialization completes are buffered in the unbounded channel.
        _owner.Tell(new ClientStreamOwner.CreateStreamInstance(
            clientOptions,
            requestOptionsFactory,
            descriptor,
            _requests.Reader,
            _responses.Writer));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _requests.Writer.TryComplete();
        _responses.Writer.TryComplete();

        // Signal the Owner to shut down gracefully. It waits for pending work
        // to drain (up to 5s), then stops the instance and itself.
        _owner.GracefulStop(TimeSpan.FromSeconds(5), new ClientStreamOwner.Shutdown());
    }

    /// <summary>
    /// Returns a <see cref="Task"/> that completes when the owner actor has fully stopped.
    /// Sends a <see cref="ClientStreamOwner.Shutdown"/> message (harmlessly ignored if already shutting down)
    /// and waits for actor termination up to <paramref name="timeout"/>.
    /// </summary>
    internal Task WhenTerminatedAsync(TimeSpan timeout)
        => _owner.GracefulStop(timeout, new ClientStreamOwner.Shutdown());
}