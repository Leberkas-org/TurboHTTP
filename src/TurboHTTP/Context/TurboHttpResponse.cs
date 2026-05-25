using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Context;

public sealed class TurboHttpResponse : HttpResponse
{
    private IFeatureCollection _features;
    private HttpContext? _httpContext;
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

    public override HttpContext HttpContext => _httpContext!;

    internal void SetHttpContext(HttpContext context)
    {
        _httpContext = context;
    }

    public override int StatusCode
    {
        get => ResponseFeature.StatusCode;
        set => ResponseFeature.StatusCode = value;
    }

    public override IHeaderDictionary Headers => ResponseFeature.Headers;

    public override Stream Body
    {
        get => BodyFeature?.Stream ?? Stream.Null;
        set { }
    }

    public override PipeWriter BodyWriter => BodyFeature?.Writer ?? throw new InvalidOperationException("IHttpResponseBodyFeature not found in feature collection");

    public override long? ContentLength
    {
        get => Headers.ContentLength;
        set => Headers.ContentLength = value;
    }

    public override string? ContentType
    {
        get => Headers["Content-Type"].ToString();
        set => Headers["Content-Type"] = value ?? string.Empty;
    }

    public override IResponseCookies Cookies
        => throw new NotSupportedException("Response cookies not yet supported.");

    public override bool HasStarted => ResponseFeature.HasStarted;

    public override void OnStarting(Func<object, Task> callback, object state)
    {
        ResponseFeature.OnStarting(callback, state);
    }

    public override void OnCompleted(Func<object, Task> callback, object state)
    {
        ResponseFeature.OnCompleted(callback, state);
    }

    public override void Redirect(string location, bool permanent = false)
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

        var feature = HttpContext.Features.Get<IHttpResponseTrailersFeature>();
        if (feature is null)
        {
            throw new InvalidOperationException(
                "Response trailers are only supported on HTTP/2 and HTTP/3 connections.");
        }

        feature.Trailers.Append(name, value);
    }

    public IHeaderDictionary GetTrailers()
    {
        var feature = HttpContext.Features.Get<IHttpResponseTrailersFeature>();
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
