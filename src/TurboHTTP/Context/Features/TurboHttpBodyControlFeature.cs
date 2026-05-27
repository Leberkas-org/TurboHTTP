using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpBodyControlFeature : IHttpBodyControlFeature
{
    public bool AllowSynchronousIO { get; set; }
}
