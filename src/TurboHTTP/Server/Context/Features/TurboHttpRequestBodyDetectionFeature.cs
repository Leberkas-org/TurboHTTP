using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpRequestBodyDetectionFeature(bool canHaveBody)
    : IHttpRequestBodyDetectionFeature
{
    public bool CanHaveBody { get; } = canHaveBody;
}