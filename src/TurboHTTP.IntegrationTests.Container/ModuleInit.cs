using System.Runtime.CompilerServices;

namespace TurboHTTP.IntegrationTests.Container;

internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        ThreadPool.SetMinThreads(512, 512);
    }
}
