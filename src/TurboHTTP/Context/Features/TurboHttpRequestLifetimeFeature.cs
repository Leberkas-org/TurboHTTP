using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpRequestLifetimeFeature : IHttpRequestLifetimeFeature
{
    private readonly RequestContext _context;

    public TurboHttpRequestLifetimeFeature(RequestContext context)
    {
        _context = context;
    }

    public CancellationToken RequestAborted
    {
        get => _context.RequestAborted;
        set => _context.RequestAborted = value;
    }

    public void Abort() => _context.Abort();
}
