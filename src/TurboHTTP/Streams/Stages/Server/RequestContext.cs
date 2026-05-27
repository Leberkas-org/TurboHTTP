using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class RequestContext
{
    public IFeatureCollection Features { get; set; } = null!;
    public CancellationTokenSource? Lifetime { get; set; }

    public CancellationToken RequestAborted { get; set; }

    public string TraceIdentifier
    {
        get => field ??= Guid.NewGuid().ToString("N");
        set;
    }

    public TurboHttpRequest Request => field ??= new TurboHttpRequest(Features);
    public TurboHttpResponse Response => field ??= new TurboHttpResponse(Features);

    public void Abort() => RequestAborted = new CancellationToken(true);
}