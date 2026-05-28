using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpRequestFeature : IHttpRequestFeature
{
    private readonly TurboResponseHeaderDictionary _headers = new();

    public string Protocol { get; set; } = "HTTP/1.1";

    public string Scheme { get; set; } = "http";

    public string Method { get; set; } = "GET";

    public string PathBase { get; set; } = string.Empty;

    public string Path { get; set; } = "/";

    public string QueryString { get; set; } = string.Empty;

    public string RawTarget { get; set; } = "/";

    public Stream Body { get; set; } = Stream.Null;

    public IHeaderDictionary Headers
    {
        get => _headers;
        set
        {
            if (value is not null)
            {
                _headers.Clear();
                foreach (var kvp in value)
                {
                    _headers[kvp.Key] = kvp.Value;
                }
            }
        }
    }

    internal string? ExtractedHost { get; set; }
}
