using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Context.Adapters;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpResponseTrailersFeature : IHttpResponseTrailersFeature
{
    private TurboResponseHeaderDictionary _trailers = new();

    public IHeaderDictionary Trailers
    {
        get => _trailers;
        set { }
    }

    public IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>> GetAllowedTrailers()
    {
        foreach (var header in _trailers)
        {
            if (TrailerFieldValidator.IsAllowedInTrailer(header.Key))
            {
                yield return header;
            }
        }
    }

    internal void Reset()
    {
        _trailers.Clear();
    }
}
