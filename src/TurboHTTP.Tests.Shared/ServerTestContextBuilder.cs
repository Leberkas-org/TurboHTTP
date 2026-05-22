using System.Text;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Shared;

internal sealed class ServerTestContextBuilder
{
    private string _method = "GET";
    private string _scheme = "http";
    private string _host = "localhost";
    private string _path = "/";
    private string _queryString = "";
    private string _protocol = "HTTP/1.1";
    private readonly HeaderDictionary _headers = new();
    private Stream _body = Stream.Null;
    private Source<ReadOnlyMemory<byte>, NotUsed>? _bodySource;
    private TurboConnectionInfo? _connection;
    private IServiceProvider? _services;
    private CancellationToken _cancellationToken;
    private IMaterializer? _materializer;

    public ServerTestContextBuilder Get(string path) => Method("GET").Path(path);
    public ServerTestContextBuilder Post(string path) => Method("POST").Path(path);
    public ServerTestContextBuilder Put(string path) => Method("PUT").Path(path);
    public ServerTestContextBuilder Delete(string path) => Method("DELETE").Path(path);

    public ServerTestContextBuilder Method(string method)
    {
        _method = method;
        return this;
    }

    public ServerTestContextBuilder Path(string path)
    {
        var qIndex = path.IndexOf('?');
        if (qIndex >= 0)
        {
            _path = path[..qIndex];
            _queryString = path[qIndex..];
        }
        else
        {
            _path = path;
            _queryString = "";
        }

        return this;
    }

    public ServerTestContextBuilder Scheme(string scheme)
    {
        _scheme = scheme;
        return this;
    }

    public ServerTestContextBuilder Host(string host)
    {
        _host = host;
        return this;
    }

    public ServerTestContextBuilder Protocol(string protocol)
    {
        _protocol = protocol;
        return this;
    }

    public ServerTestContextBuilder Header(string name, string value)
    {
        _headers[name] = value;
        return this;
    }

    public ServerTestContextBuilder Body(Stream body)
    {
        _body = body;
        return this;
    }

    public ServerTestContextBuilder Body(byte[] data)
    {
        _body = new MemoryStream(data);
        return this;
    }

    public ServerTestContextBuilder BodySource(Source<ReadOnlyMemory<byte>, NotUsed> source)
    {
        _bodySource = source;
        return this;
    }

    public ServerTestContextBuilder FormBody(string urlEncodedData)
    {
        _headers["Content-Type"] = "application/x-www-form-urlencoded";
        _body = new MemoryStream(Encoding.UTF8.GetBytes(urlEncodedData));
        return this;
    }

    public ServerTestContextBuilder MultipartBody(Action<MultipartFormDataContent> configure)
    {
        var content = new MultipartFormDataContent();
        configure(content);
        var stream = new MemoryStream();
        content.CopyTo(stream, null, CancellationToken.None);
        stream.Position = 0;
        _body = stream;
        _headers["Content-Type"] = content.Headers.ContentType!.ToString();
        return this;
    }

    public ServerTestContextBuilder JsonBody(string json)
    {
        _headers["Content-Type"] = "application/json";
        _body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return this;
    }

    public ServerTestContextBuilder Connection(TurboConnectionInfo connection)
    {
        _connection = connection;
        return this;
    }

    public ServerTestContextBuilder Services(IServiceProvider services)
    {
        _services = services;
        return this;
    }

    public ServerTestContextBuilder RequestAborted(CancellationToken token)
    {
        _cancellationToken = token;
        return this;
    }

    public ServerTestContextBuilder Materializer(IMaterializer materializer)
    {
        _materializer = materializer;
        return this;
    }

    public TurboHttpRequestFeature BuildRequestFeature()
    {
        var rawTarget = string.IsNullOrEmpty(_queryString)
            ? _path
            : string.Concat(_path, _queryString);

        return new TurboHttpRequestFeature
        {
            Method = _method,
            Scheme = _scheme,
            Path = _path,
            QueryString = _queryString,
            RawTarget = rawTarget,
            Protocol = _protocol,
            Headers = _headers,
            Body = _body,
            BodySource = _bodySource ?? Source.Empty<ReadOnlyMemory<byte>>(),
            ExtractedHost = _host
        };
    }

    public TurboHttpContext Build()
    {
        var conn = _connection ?? new TurboConnectionInfo("test", null, 0, null, 0);

        var features = new FeatureCollection();
        var requestFeature = BuildRequestFeature();
        features.Set<IHttpRequestFeature>(requestFeature);
        features.Set<ITurboRequestBodyFeature>(requestFeature);
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpConnectionFeature>(new TurboHttpConnectionFeature(conn));
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<ITurboResponseBodyFeature>(bodyFeature);

        return new TurboHttpContext(features, conn, _services, _cancellationToken, _materializer!);
    }
}
