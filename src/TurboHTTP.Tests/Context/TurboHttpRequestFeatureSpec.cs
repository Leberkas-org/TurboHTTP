using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Tests.Context;

public sealed class TurboHttpRequestFeatureSpec
{
    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_have_default_protocol()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.Equal("HTTP/1.1", feature.Protocol);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_have_default_scheme()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.Equal("http", feature.Scheme);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_have_default_method()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.Equal("GET", feature.Method);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_have_default_pathbase()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.Equal(string.Empty, feature.PathBase);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_have_default_path()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.Equal("/", feature.Path);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_have_default_querystring()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.Equal(string.Empty, feature.QueryString);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_have_default_rawtarget()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.Equal("/", feature.RawTarget);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_have_nonempty_headers()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.NotNull(feature.Headers);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_have_null_body()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.Equal(Stream.Null, feature.Body);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_be_settable_protocol()
    {
        var feature = new TurboHttpRequestFeature { Protocol = "HTTP/2" };
        Assert.Equal("HTTP/2", feature.Protocol);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_be_settable_method()
    {
        var feature = new TurboHttpRequestFeature { Method = "POST" };
        Assert.Equal("POST", feature.Method);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_be_settable_path()
    {
        var feature = new TurboHttpRequestFeature { Path = "/users" };
        Assert.Equal("/users", feature.Path);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_implement_ihttp_request_feature()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.IsAssignableFrom<IHttpRequestFeature>(feature);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_implement_iturorequestbodyfeature()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.IsAssignableFrom<ITurboRequestBodyFeature>(feature);
    }

    [Fact(Timeout = 5000)]
    public void FromHttpRequestMessage_should_map_method()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/test");
        var feature = TurboHttpRequestFeature.FromHttpRequestMessage(request, Source.Empty<ReadOnlyMemory<byte>>());
        Assert.Equal("POST", feature.Method);
    }

    [Fact(Timeout = 5000)]
    public void FromHttpRequestMessage_should_map_path()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/users");
        var feature = TurboHttpRequestFeature.FromHttpRequestMessage(request, Source.Empty<ReadOnlyMemory<byte>>());
        Assert.Equal("/api/users", feature.Path);
    }

    [Fact(Timeout = 5000)]
    public void FromHttpRequestMessage_should_map_querystring()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test?page=1&size=10");
        var feature = TurboHttpRequestFeature.FromHttpRequestMessage(request, Source.Empty<ReadOnlyMemory<byte>>());
        Assert.Equal("?page=1&size=10", feature.QueryString);
    }

    [Fact(Timeout = 5000)]
    public void FromHttpRequestMessage_should_map_scheme()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/test");
        var feature = TurboHttpRequestFeature.FromHttpRequestMessage(request, Source.Empty<ReadOnlyMemory<byte>>());
        Assert.Equal("https", feature.Scheme);
    }

    [Fact(Timeout = 5000)]
    public void FromHttpRequestMessage_should_map_protocol()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test") { Version = new Version(2, 0) };
        var feature = TurboHttpRequestFeature.FromHttpRequestMessage(request, Source.Empty<ReadOnlyMemory<byte>>());
        Assert.Equal("HTTP/2", feature.Protocol);
    }

    [Fact(Timeout = 5000)]
    public void FromHttpRequestMessage_should_map_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        request.Headers.Add("X-Custom", "value");
        var feature = TurboHttpRequestFeature.FromHttpRequestMessage(request, Source.Empty<ReadOnlyMemory<byte>>());
        Assert.Equal("value", feature.Headers["X-Custom"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void FromHttpRequestMessage_should_map_rawtarget()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test?a=1");
        var feature = TurboHttpRequestFeature.FromHttpRequestMessage(request, Source.Empty<ReadOnlyMemory<byte>>());
        Assert.Contains("/test", feature.RawTarget);
    }

    [Fact(Timeout = 5000)]
    public void FromHttpRequestMessage_should_expose_body_source()
    {
        var source = Source.Single(new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }));
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/test");
        var feature = TurboHttpRequestFeature.FromHttpRequestMessage(request, source);
        var bodyFeature = (ITurboRequestBodyFeature)feature;
        Assert.Same(source, bodyFeature.BodySource);
    }
}
