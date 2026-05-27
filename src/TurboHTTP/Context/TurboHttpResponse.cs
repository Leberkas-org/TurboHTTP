using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Context;

public sealed class TurboHttpResponse
{
    private IFeatureCollection _features;
    private TurboHttpContext? _httpContext;
    private IHttpResponseFeature? _responseFeature;
    private IHttpResponseBodyFeature? _bodyFeature;

    public TurboHttpResponse(IFeatureCollection features)
    {
        _features = features ?? throw new ArgumentNullException(nameof(features));
    }

    private IHttpResponseFeature ResponseFeature
        => _responseFeature ??= _features.Get<IHttpResponseFeature>() ?? throw new InvalidOperationException("IHttpResponseFeature not found in feature collection");

    private IHttpResponseBodyFeature? BodyFeature
        => _bodyFeature ??= _features.Get<IHttpResponseBodyFeature>();

    public TurboHttpContext HttpContext => _httpContext!;

    internal void SetHttpContext(TurboHttpContext context)
    {
        _httpContext = context;
    }

    public int StatusCode
    {
        get => ResponseFeature.StatusCode;
        set => ResponseFeature.StatusCode = value;
    }

    public IHeaderDictionary Headers => ResponseFeature.Headers;

    public Stream Body
    {
        get => BodyFeature?.Stream ?? Stream.Null;
        set { }
    }

    public PipeWriter BodyWriter => BodyFeature?.Writer ?? throw new InvalidOperationException("IHttpResponseBodyFeature not found in feature collection");

    public long? ContentLength
    {
        get => Headers.ContentLength;
        set => Headers.ContentLength = value;
    }

    public string? ContentType
    {
        get => Headers["Content-Type"].ToString();
        set => Headers["Content-Type"] = value ?? string.Empty;
    }

    public bool HasStarted => ResponseFeature.HasStarted;

    public void OnStarting(Func<object, Task> callback, object state)
    {
        ResponseFeature.OnStarting(callback, state);
    }

    public void OnCompleted(Func<object, Task> callback, object state)
    {
        ResponseFeature.OnCompleted(callback, state);
    }

    public void Redirect(string location, bool permanent = false)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (location.AsSpan().ContainsAny('\r', '\n'))
        {
            throw new ArgumentException("Redirect location must not contain CR or LF characters.", nameof(location));
        }

        if (!location.StartsWith('/') &&
            Uri.TryCreate(location, UriKind.Absolute, out var uri) &&
            uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Redirect location must be a relative path or an HTTP/HTTPS URL.", nameof(location));
        }

        StatusCode = permanent ? 301 : 302;
        Headers["Location"] = location;
    }

    public void DeclareTrailer(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var existing = Headers["Trailer"].ToString();
        Headers["Trailer"] = string.IsNullOrEmpty(existing)
            ? name
            : string.Concat(existing, ", ", name);
    }

    public void AppendTrailer(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        var feature = _features.Get<IHttpResponseTrailersFeature>();
        if (feature is null)
        {
            throw new InvalidOperationException(
                "Response trailers are only supported on HTTP/2 and HTTP/3 connections.");
        }

        feature.Trailers.Append(name, value);
    }

    public IHeaderDictionary GetTrailers()
    {
        var feature = _features.Get<IHttpResponseTrailersFeature>();
        return feature?.Trailers ?? new HeaderDictionary();
    }

    internal void Reset(IFeatureCollection features)
    {
        _features = features;
        _responseFeature = null;
        _bodyFeature = null;
        if (features.Get<IHttpResponseFeature>() is TurboHttpResponseFeature turboFeature)
        {
            turboFeature.Reset();
        }
        if (features.Get<IHttpResponseTrailersFeature>() is TurboHttpResponseTrailersFeature trailersFeature)
        {
            trailersFeature.Reset();
        }
    }
}
