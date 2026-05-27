using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpRequestIdentifierFeature : IHttpRequestIdentifierFeature
{
    private readonly RequestContext _context;

    public TurboHttpRequestIdentifierFeature(RequestContext context)
    {
        _context = context;
    }

    public string TraceIdentifier
    {
        get => _context.TraceIdentifier;
        set => _context.TraceIdentifier = value;
    }
}
