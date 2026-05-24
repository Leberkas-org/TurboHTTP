using System.IO.Pipelines;
using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using TurboHTTP.Context.Adapters;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Context;

public sealed class TurboHttpRequest : HttpRequest
{
    private IFeatureCollection _features;
    private HttpContext? _httpContext;
    private IFormCollection? _parsedForm;
    private Uri? _cachedRequestUri;
    private IHttpRequestFeature? _requestFeature;
    private IQueryCollection? _query;
    private IRequestCookieCollection? _cookies;
    private RouteValueDictionary? _routeValues;
    private PipeReader? _bodyReader;

    public TurboHttpRequest(IFeatureCollection features)
    {
        _features = features ?? throw new ArgumentNullException(nameof(features));
    }

    private IHttpRequestFeature RequestFeature
        => _requestFeature ??= _features.Get<IHttpRequestFeature>() ??
                     throw new InvalidOperationException("IHttpRequestFeature not found in feature collection");

    public override HttpContext HttpContext => _httpContext!;

    internal void SetHttpContext(HttpContext context)
    {
        _httpContext = context;
    }

    public Uri? RequestUri
    {
        get
        {
            if (_cachedRequestUri is not null)
            {
                return _cachedRequestUri;
            }

            var host = Host.Value;
            if (string.IsNullOrEmpty(host))
            {
                return null;
            }

            var uriString = string.Concat(Scheme, "://", host, Path.Value, QueryString.Value);
            _cachedRequestUri = new Uri(uriString);
            return _cachedRequestUri;
        }
    }

    public HttpContent? Content
    {
        get
        {
            var feature = RequestFeature;
            return feature.Body != Stream.Null ? new StreamContent(feature.Body) : null;
        }
    }

    public override string Method
    {
        get => RequestFeature.Method;
        set => RequestFeature.Method = value;
    }

    public override string Scheme
    {
        get => RequestFeature.Scheme;
        set => RequestFeature.Scheme = value;
    }

    public override bool IsHttps
    {
        get => Scheme == "https";
        set => Scheme = value ? "https" : "http";
    }

    public override HostString Host
    {
        get
        {
            var hostHeader = (string?)Headers["Host"] ?? string.Empty;
            if (string.IsNullOrEmpty(hostHeader))
            {
                // Fallback to extracted host from RequestUri if Host header is not set
                var feature = RequestFeature;
                if (feature is TurboHttpRequestFeature turboFeature && !string.IsNullOrEmpty(turboFeature.ExtractedHost))
                {
                    return new HostString(turboFeature.ExtractedHost);
                }
            }
            return new HostString(hostHeader);
        }
        set => Headers["Host"] = value.Value ?? string.Empty;
    }

    public override PathString PathBase
    {
        get => new(RequestFeature.PathBase);
        set => RequestFeature.PathBase = value.Value ?? string.Empty;
    }

    public override PathString Path
    {
        get => new(RequestFeature.Path);
        set => RequestFeature.Path = value.Value ?? "/";
    }

    public override QueryString QueryString
    {
        get => new(RequestFeature.QueryString);
        set => RequestFeature.QueryString = value.Value ?? string.Empty;
    }

    public override IQueryCollection Query
    {
        get
        {
            _query ??= new TurboQueryCollection(RequestFeature.QueryString);
            return _query;
        }
        set => _query = value;
    }

    public override string Protocol
    {
        get => RequestFeature.Protocol;
        set => RequestFeature.Protocol = value;
    }

    public override IHeaderDictionary Headers => RequestFeature.Headers;

    public override IRequestCookieCollection Cookies
    {
        get
        {
            _cookies ??= new TurboRequestCookieCollection(Headers["Cookie"].ToString());
            return _cookies;
        }
        set => _cookies = value;
    }

    public override long? ContentLength
    {
        get => Headers.ContentLength;
        set => Headers.ContentLength = value;
    }

    public override string? ContentType
    {
        get => (string?)Headers["Content-Type"] ?? string.Empty;
        set => Headers["Content-Type"] = value ?? string.Empty;
    }

    public override Stream Body
    {
        get => RequestFeature.Body;
        set => RequestFeature.Body = value;
    }

    public override PipeReader BodyReader
    {
        get
        {
            _bodyReader ??= PipeReader.Create(Body);
            return _bodyReader;
        }
    }

    public Source<ReadOnlyMemory<byte>, NotUsed> BodySource
        => _features.Get<ITurboRequestBodyFeature>()?.BodySource ?? Source.Empty<ReadOnlyMemory<byte>>();

    public override bool HasFormContentType
    {
        get
        {
            var contentType = ContentType;
            if (string.IsNullOrEmpty(contentType))
            {
                return false;
            }

            return contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
                   || contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase);
        }
    }

    public override IFormCollection Form
    {
        get => _parsedForm ?? throw new InvalidOperationException("Form has not been read. Call ReadFormAsync first.");
        set => _parsedForm = value;
    }

    public override async Task<IFormCollection> ReadFormAsync(CancellationToken cancellationToken = default)
    {
        if (_parsedForm is not null)
        {
            return _parsedForm;
        }

        var contentType = ContentType;
        if (string.IsNullOrEmpty(contentType))
        {
            _parsedForm = EmptyForm();
            return _parsedForm;
        }

        if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            _parsedForm = await ParseUrlEncodedFormAsync(cancellationToken);
            return _parsedForm;
        }

        if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            _parsedForm = await ParseMultipartFormAsync(contentType, cancellationToken);
            return _parsedForm;
        }

        _parsedForm = EmptyForm();
        return _parsedForm;
    }

    public override RouteValueDictionary RouteValues
    {
        get => _routeValues ??= new RouteValueDictionary();
        set => _routeValues = value;
    }

    private static IFormCollection EmptyForm()
    {
        return new TurboFormCollection(
            new Dictionary<string, StringValues>(),
            new TurboFormFileCollection([]));
    }

    private async Task<IFormCollection> ParseUrlEncodedFormAsync(CancellationToken ct)
    {
        var feature = RequestFeature;
        if (feature.Body == Stream.Null)
        {
            return EmptyForm();
        }

        using var reader = new StreamReader(feature.Body);
        var body = await reader.ReadToEndAsync(ct);
        var fields = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2)
            {
                var key = Uri.UnescapeDataString(kv[0]);
                var value = Uri.UnescapeDataString(kv[1]);
                if (fields.TryGetValue(key, out var existing))
                {
                    fields[key] = StringValues.Concat(existing, value);
                }
                else
                {
                    fields[key] = value;
                }
            }
        }

        return new TurboFormCollection(fields, new TurboFormFileCollection([]));
    }

    private async Task<IFormCollection> ParseMultipartFormAsync(string contentType, CancellationToken ct)
    {
        var feature = RequestFeature;
        if (feature.Body == Stream.Null)
        {
            return EmptyForm();
        }

        var boundary = ExtractBoundary(contentType);
        if (boundary is null)
        {
            return EmptyForm();
        }

        var fields = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        var files = new List<IFormFile>();

        var reader = new MultipartReader(boundary, feature.Body);

        var section = await reader.ReadNextSectionAsync(ct);
        while (section is not null)
        {
            if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
            {
                if (disposition.IsFileDisposition())
                {
                    var fileContent = new MemoryStream();
                    await section.Body.CopyToAsync(fileContent, ct);
                    files.Add(new TurboFormFile(
                        disposition.Name.Value ?? string.Empty,
                        disposition.FileName.Value ?? string.Empty,
                        section.ContentType ?? "application/octet-stream",
                        fileContent.ToArray()));
                }
                else if (disposition.IsFormDisposition())
                {
                    using var sr = new StreamReader(section.Body);
                    var value = await sr.ReadToEndAsync(ct);
                    var name = disposition.Name.Value ?? string.Empty;
                    if (fields.TryGetValue(name, out var existing))
                    {
                        fields[name] = StringValues.Concat(existing, value);
                    }
                    else
                    {
                        fields[name] = value;
                    }
                }
            }

            section = await reader.ReadNextSectionAsync(ct);
        }

        return new TurboFormCollection(fields, new TurboFormFileCollection(files));
    }

    private static string? ExtractBoundary(string contentType)
    {
        var parts = contentType.Split(';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["boundary=".Length..].Trim('"');
            }
        }

        return null;
    }

    internal void Reset(IFeatureCollection features)
    {
        _features = features;
        _requestFeature = null;
        _cachedRequestUri = null;
        _parsedForm = null;
        _query = null;
        _cookies = null;
        _routeValues = null;
        _bodyReader = null;
    }
}