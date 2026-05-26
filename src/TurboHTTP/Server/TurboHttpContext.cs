using System.Security.Claims;
using Akka.Streams;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public sealed class TurboHttpContext : HttpContext
{
    private static readonly ClaimsPrincipal AnonymousPrincipal = new();

    private IFeatureCollection _features;
    private TurboConnectionInfo _connectionInfo;
    private ClaimsPrincipal? _user;
    private IDictionary<object, object?>? _items;
    private string? _traceIdentifier;
    private TurboEndpointMetadata? _endpointMetadata;

    public TurboHttpContext(
        IFeatureCollection features,
        TurboConnectionInfo connectionInfo,
        IServiceProvider? services,
        CancellationToken requestAborted,
        IMaterializer materializer)
    {
        _features = features;
        _connectionInfo = connectionInfo;
        RequestServices = services!;
        RequestAborted = requestAborted;
        Materializer = materializer;

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

    public override IFeatureCollection Features => _features;

    public override HttpRequest Request => TurboRequest;
    public TurboHttpRequest TurboRequest { get; }

    public override HttpResponse Response => TurboResponse;
    public TurboHttpResponse TurboResponse { get; }
    public override ConnectionInfo Connection => _connectionInfo;
    public override WebSocketManager WebSockets
        => throw new NotSupportedException(
            "TurboHTTP does not support WebSockets. Use Akka.Streams for bidirectional streaming.");

    public override ClaimsPrincipal User
    {
        get => _user ?? AnonymousPrincipal;
        set => _user = value;
    }

    public override IDictionary<object, object?> Items
    {
        get => _items ??= new Dictionary<object, object?>();
        set => _items = value;
    }

    public override IServiceProvider RequestServices { get; set; }
    public override CancellationToken RequestAborted { get; set; }

    public override string TraceIdentifier
    {
        get => _traceIdentifier ??= Guid.NewGuid().ToString("N");
        set => _traceIdentifier = value;
    }

    public override ISession Session
    {
        get => throw new NotSupportedException(
            "TurboHTTP does not support ASP.NET Core sessions. Use ITurboMiddleware with context.Items for per-request state.");
        set => throw new NotSupportedException(
            "TurboHTTP does not support ASP.NET Core sessions. Use ITurboMiddleware with context.Items for per-request state.");
    }

    public override void Abort() => RequestAborted = new CancellationToken(true);

    public IMaterializer Materializer { get; set; } = null!;

    internal TurboEndpointMetadata? EndpointMetadata
    {
        get => _endpointMetadata;
        set => _endpointMetadata = value;
    }

    internal void Reset(
        IFeatureCollection features,
        TurboConnectionInfo connectionInfo,
        IServiceProvider? services,
        CancellationToken requestAborted,
        IMaterializer materializer)
    {
        _features = features;
        _connectionInfo = connectionInfo;
        _user = null;
        _items = null;
        _traceIdentifier = null;
        _endpointMetadata = null;
        RequestAborted = requestAborted;
        RequestServices = services!;
        Materializer = materializer;

        TurboRequest.Reset(features);
        TurboRequest.SetHttpContext(this);
        TurboResponse.Reset(features);
        TurboResponse.SetHttpContext(this);
    }
}