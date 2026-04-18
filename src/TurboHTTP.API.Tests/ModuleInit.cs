using System.Runtime.CompilerServices;

namespace TurboHTTP.API.Tests;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyDiffPlex.Initialize();
        VerifierSettings.ScrubLinesContaining("[assembly: ReleaseDateAttribute(");
        UseProjectRelativeDirectory("verify");
        VerifierSettings.UniqueForRuntime();
        VerifierSettings.InitializePlugins();
    }
}