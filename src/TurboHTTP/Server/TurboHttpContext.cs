using System.Security.Claims;
using Akka.Streams;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;

namespace TurboHTTP.Server;

public sealed class TurboHttpContext : HttpContext
{
    private readonly TurboConnectionInfo _connectionInfo;

    public TurboHttpContext(
        IFeatureCollection features,
        TurboConnectionInfo connectionInfo,
        IServiceProvider? services,
        CancellationToken requestAborted,
        IMaterializer materializer)
    {
        Features = features;
        _connectionInfo = connectionInfo;
        RequestServices = services!;
        RequestAborted = requestAborted;
        Materializer = materializer;
        TraceIdentifier = Guid.NewGuid().ToString("N");

        TurboRequest = new TurboHttpRequest(features);
        TurboRequest.SetHttpContext(this);
        TurboResponse = new TurboHttpResponse(features);
        TurboResponse.SetHttpContext(this);
    }

    internal TurboHttpContext(IFeatureCollection features)
        : this(
            features,
            new TurboConnectionInfo(Guid.NewGuid().ToString("N"), null, 0, null, 0),
            services: null,
            requestAborted: CancellationToken.None,
            materializer: null!)
    {
    }

    public override IFeatureCollection Features { get; }

    public override HttpRequest Request => TurboRequest;
    public TurboHttpRequest TurboRequest { get; }

    public override HttpResponse Response => TurboResponse;
    public TurboHttpResponse TurboResponse { get; }
    public override ConnectionInfo Connection => _connectionInfo;
    public override WebSocketManager WebSockets => throw new NotSupportedException("WebSockets are not yet supported.");
    public override ClaimsPrincipal User { get; set; } = new();
    public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();

    public override IServiceProvider RequestServices { get; set; }
    public override CancellationToken RequestAborted { get; set; }
    public override string TraceIdentifier { get; set; }

    public override ISession Session
    {
        get => throw new NotSupportedException("Sessions are not yet supported.");
        set => throw new NotSupportedException("Sessions are not yet supported.");
    }

    public override void Abort() => RequestAborted = new CancellationToken(true);

    public IMaterializer Materializer { get; internal init; }
}