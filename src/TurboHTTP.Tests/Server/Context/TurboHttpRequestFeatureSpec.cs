using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server.Context;

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
}