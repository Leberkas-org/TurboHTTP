using System.Runtime.CompilerServices;
using Servus.Akka.IO;

namespace TurboHTTP.Tests;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        NetworkBuffer.ConfigurePoolSize(0);
    }
}
