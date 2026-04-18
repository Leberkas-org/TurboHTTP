using System.Runtime.CompilerServices;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        NetworkBuffer.ConfigurePoolSize(0);
    }
}
