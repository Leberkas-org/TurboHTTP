using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpRequestIdentifierFeature : IHttpRequestIdentifierFeature
{
    private readonly TurboHttpContext _context;

    public TurboHttpRequestIdentifierFeature(TurboHttpContext context)
    {
        _context = context;
    }

    public string TraceIdentifier
    {
        get => _context.TraceIdentifier;
        set => _context.TraceIdentifier = value;
    }
}
