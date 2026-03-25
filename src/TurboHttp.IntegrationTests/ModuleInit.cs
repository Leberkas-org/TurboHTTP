using System.Runtime.CompilerServices;

namespace TurboHttp.IntegrationTests;

internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        // Akka.NET dispatcher threads + xUnit worker threads can exhaust
        // the default thread pool (min = ProcessorCount).  Bump high so
        // HTTP/1.0 tests — which create a fresh ActorSystem per request —
        // don't stall waiting for threads during sustained test runs.
        ThreadPool.SetMinThreads(256, 256);
    }
}
