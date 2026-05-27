using System.Security.Claims;
using Akka.Streams;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;

namespace TurboHTTP.Server;

public sealed class TurboHttpContext
{
    private static readonly ClaimsPrincipal AnonymousPrincipal = new();

    private IFeatureCollection _features;
    private ClaimsPrincipal? _user;
    private IDictionary<object, object?>? _items;
    private string? _traceIdentifier;

    public TurboHttpContext(
        IFeatureCollection features,
        TurboConnectionInfo connectionInfo,
        IServiceProvider? services,
        CancellationToken requestAborted,
        IMaterializer materializer)
    {
        _features = features;
        Connection = connectionInfo;
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

    public IFeatureCollection Features => _features;

    public TurboHttpRequest Request => TurboRequest;
    public TurboHttpRequest TurboRequest { get; }

    public TurboHttpResponse Response => TurboResponse;
    public TurboHttpResponse TurboResponse { get; }
    public TurboConnectionInfo Connection { get; private set; }

    public ClaimsPrincipal User
    {
        get => _user ?? AnonymousPrincipal;
        set => _user = value;
    }

    public IDictionary<object, object?> Items
    {
        get => _items ??= new Dictionary<object, object?>();
        set => _items = value;
    }

    public IServiceProvider RequestServices { get; set; }
    public CancellationToken RequestAborted { get; set; }

    public string TraceIdentifier
    {
        get => _traceIdentifier ??= Guid.NewGuid().ToString("N");
        set => _traceIdentifier = value;
    }

    public void Abort() => RequestAborted = new CancellationToken(true);

    public IMaterializer Materializer { get; set; } = null!;

    internal void Reset(
        IFeatureCollection features,
        TurboConnectionInfo connectionInfo,
        IServiceProvider? services,
        CancellationToken requestAborted,
        IMaterializer materializer)
    {
        _features = features;
        Connection = connectionInfo;
        _user = null;
        _items = null;
        _traceIdentifier = null;
        RequestAborted = requestAborted;
        RequestServices = services!;
        Materializer = materializer;

        TurboRequest.Reset(features);
        TurboRequest.SetHttpContext(this);
        TurboResponse.Reset(features);
        TurboResponse.SetHttpContext(this);
    }
}