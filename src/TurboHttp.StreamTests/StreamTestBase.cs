using Akka.Actor;
using Akka.Streams;
using Akka.TestKit.Xunit2;

namespace TurboHttp.StreamTests;

public abstract class StreamTestBase : TestKit
{
    protected readonly IMaterializer Materializer;

    protected StreamTestBase() : base(ActorSystem.Create("st-" + Guid.NewGuid()))
    {
        Materializer = Sys.Materializer();
    }
}
