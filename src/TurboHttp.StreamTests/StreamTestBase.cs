using Akka.Actor;
using Akka.Streams;
using Akka.TestKit.Xunit;

namespace TurboHttp.StreamTests;

/// <summary>
/// Abstract base class for all Akka.Streams stage tests.
/// Creates a fresh ActorSystem and IMaterializer per test and tears them down on dispose.
/// </summary>
/// <remarks>
/// Inherits from TestKit; actor system name includes a Guid to avoid port conflicts between parallel tests.
/// Ensures materializer is shut down cleanly before actor system terminates to prevent resource leaks.
/// </remarks>
public abstract class StreamTestBase : TestKit
{
    protected readonly IMaterializer Materializer;

    protected StreamTestBase() : base(ActorSystem.Create("st-" + Guid.NewGuid()))
    {
        Materializer = Sys.Materializer();
    }
}