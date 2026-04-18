using System.Runtime.CompilerServices;
using TurboHTTP.Internal;

namespace TurboHTTP.AcceptanceTests;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        NetworkBuffer.ConfigurePoolSize(0);
    }
}
