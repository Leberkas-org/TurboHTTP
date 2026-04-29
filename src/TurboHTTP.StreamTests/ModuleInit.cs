using System.Runtime.CompilerServices;
using Servus.Akka.Transport;

namespace TurboHTTP.StreamTests;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        TransportBuffer.ConfigurePoolSize(0);
    }
}
