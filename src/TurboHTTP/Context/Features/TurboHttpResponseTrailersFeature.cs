using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpResponseTrailersFeature : IHttpResponseTrailersFeature
{
    public IHeaderDictionary Trailers { get; set; } = new HeaderDictionary();

    public IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>> GetAllowedTrailers()
    {
        foreach (var header in Trailers)
        {
            if (TrailerFieldValidator.IsAllowedInTrailer(header.Key))
            {
                yield return header;
            }
        }
    }

    internal void Reset()
    {
        Trailers.Clear();
    }
}
