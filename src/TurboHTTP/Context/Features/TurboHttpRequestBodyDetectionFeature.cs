using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpRequestBodyDetectionFeature(bool canHaveBody)
    : IHttpRequestBodyDetectionFeature, ITurboRequestBodyDetectionFeature
{
    public bool CanHaveBody { get; } = canHaveBody;
}