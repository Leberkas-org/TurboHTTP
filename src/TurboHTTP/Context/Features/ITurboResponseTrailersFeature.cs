using TurboHTTP.Context;

namespace TurboHTTP.Context.Features;

public interface ITurboResponseTrailersFeature
{
    ITurboHeaderDictionary Trailers { get; set; }
}
