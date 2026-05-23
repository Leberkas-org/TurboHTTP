using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpRequestLifetimeFeature : IHttpRequestLifetimeFeature
{
    private readonly TurboHttpContext _context;

    public TurboHttpRequestLifetimeFeature(TurboHttpContext context)
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
