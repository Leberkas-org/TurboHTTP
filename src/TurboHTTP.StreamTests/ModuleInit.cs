using System.Runtime.CompilerServices;
using Servus.Akka.IO;

namespace TurboHTTP.StreamTests;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        NetworkBuffer.ConfigurePoolSize(0);
    }
}
