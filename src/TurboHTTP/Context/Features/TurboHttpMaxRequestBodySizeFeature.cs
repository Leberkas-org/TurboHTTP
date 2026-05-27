using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
{
    public bool IsReadOnly { get; set; }
    public long? MaxRequestBodySize { get; set; }
}
