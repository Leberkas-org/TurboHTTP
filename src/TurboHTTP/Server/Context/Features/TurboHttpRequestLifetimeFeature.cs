using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpRequestLifetimeFeature : IHttpRequestLifetimeFeature
{
    public CancellationToken RequestAborted { get; set; }

    public void Abort() => RequestAborted = new CancellationToken(true);
}
