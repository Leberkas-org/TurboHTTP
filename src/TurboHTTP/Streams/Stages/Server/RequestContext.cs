using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class RequestContext
{
    private string? _traceIdentifier;

    public IFeatureCollection Features { get; set; } = null!;
    public CancellationTokenSource? Lifetime { get; set; }

    public CancellationToken RequestAborted { get; set; }

    public string TraceIdentifier
    {
        get => _traceIdentifier ??= Guid.NewGuid().ToString("N");
        set => _traceIdentifier = value;
    }

    public void Abort() => RequestAborted = new CancellationToken(true);
}
