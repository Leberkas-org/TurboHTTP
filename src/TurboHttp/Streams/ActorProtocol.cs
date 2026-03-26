using System;
using System.Net.Http;
using System.Threading.Channels;

namespace TurboHttp.Streams;

/// <summary>
/// Protocol messages for <c>ClientStreamOwner</c>, the actor that manages
/// stream lifecycle and supervises the stream instance.
/// </summary>
public static class ClientStreamOwner
{
    /// <summary>Base type for all messages handled by <c>ClientStreamOwner</c>.</summary>
    public abstract record Message;

    internal sealed record CreateStreamInstance(
        TurboClientOptions ClientOptions,
        Func<TurboRequestOptions> RequestOptionsFactory,
        PipelineDescriptor Pipeline,
        ChannelReader<HttpRequestMessage> RequestReader,
        ChannelWriter<HttpResponseMessage> ResponseWriter) : Message;

    public sealed record StreamInstanceCreated : Message;

    public sealed record StreamInstanceFailed(Exception Reason, int AttemptNumber) : Message;

    public sealed record Shutdown : Message;
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
