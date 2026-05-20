using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpRequestBodyDetectionFeature(HttpRequestMessage request)
    : IHttpRequestBodyDetectionFeature
{
    private readonly HttpRequestMessage _request = request ?? throw new ArgumentNullException(nameof(request));

    public bool CanHaveBody => _request.Content is not null;
}