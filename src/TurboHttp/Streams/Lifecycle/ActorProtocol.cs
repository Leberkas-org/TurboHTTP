using System.Threading.Channels;

namespace TurboHttp.Streams.Lifecycle;

/// <summary>
/// Protocol messages for <c>ClientStreamOwner</c>, the actor that manages
/// stream lifecycle and supervises the stream instance.
/// </summary>
public static class ClientStreamOwner
{
    internal sealed record CreateStreamInstance(
        TurboClientOptions ClientOptions,
        Func<TurboRequestOptions> RequestOptionsFactory,
        PipelineDescriptor Pipeline,
        ChannelReader<HttpRequestMessage> RequestReader,
        ChannelWriter<HttpResponseMessage> ResponseWriter);

    public sealed record StreamInstanceCreated;

    public sealed record StreamInstanceFailed(Exception Reason, int AttemptNumber);

    public sealed record Shutdown;
}

// ──────────────────────────────────────────────────────────────────────────────
// Message Flow Diagrams (Merged Design: Owner handles materialization directly)
// ──────────────────────────────────────────────────────────────────────────────
//
// HAPPY PATH: Create → Materialize → Run → Shutdown
// ──────────────────────────────────────────────────
//
//   StreamManager                   Owner
//       │                             │
//       │──CreateStreamInstance───────▶│
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
// ─────────────────────────────────────────────────────────
//
//   StreamManager                   Owner
//       │                             │
//       │──CreateStreamInstance───────▶│
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
// ──────────────────────────────
//
//   StreamManager                   Owner
//       │                             │
//       │──CreateStreamInstance───────▶│
//       │                             │ ... 3 failed attempts (100ms, 500ms, 2s) ...
//       │◀─StreamInstanceFailed───────│ (retries exhausted)
//       │  (propagate error)          │
//       │                             ╳
//