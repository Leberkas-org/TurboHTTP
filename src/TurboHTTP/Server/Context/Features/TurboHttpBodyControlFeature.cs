using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpBodyControlFeature : IHttpBodyControlFeature
{
    public bool AllowSynchronousIO { get; set; }
}
