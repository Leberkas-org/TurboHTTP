using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpRequestIdentifierFeature : IHttpRequestIdentifierFeature
{
    public string TraceIdentifier
    {
        get => field ??= Guid.NewGuid().ToString("N");
        set;
    }
}
