using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Context.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Context;

public sealed class TurboHttpRequestSpec
{
    [Fact(Timeout = 5000)]
    public void Method_should_delegate_to_feature()
    {
        var (request, _) = CreateRequest("POST", "/test");
        Assert.Equal("POST", request.Method);
    }

    [Fact(Timeout = 5000)]
    public void Path_should_delegate_to_feature()
    {
        var (request, _) = CreateRequest("GET", "/api/users");
        Assert.Equal("/api/users", request.Path);
    }

    [Fact(Timeout = 5000)]
    public void QueryString_should_delegate_to_feature()
    {
        var (request, _) = CreateRequest("GET", "/test?page=1");
        Assert.Equal("?page=1", request.QueryString);
    }

    [Fact(Timeout = 5000)]
    public void Scheme_should_delegate_to_feature()
    {
        var (request, _) = CreateRequest("GET", "/test", scheme: "https");
        Assert.Equal("https", request.Scheme);
    }

    [Fact(Timeout = 5000)]
    public void Protocol_should_delegate_to_feature()
    {
        var (request, _) = CreateRequest("GET", "/test");
        Assert.Equal("HTTP/1.1", request.Protocol);
    }

    [Fact(Timeout = 5000)]
    public void Headers_should_expose_request_headers()
    {
        var feature = ServerTestContext.Request()
            .Get("/test")
            .Header("X-Custom", "val")
            .BuildRequestFeature();
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(feature);
        var request = new TurboHttpRequest(features);

        Assert.Equal("val", request.Headers["X-Custom"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Query_should_parse_query_string()
    {
        var (request, _) = CreateRequest("GET", "/test?name=Alice&age=30");
        Assert.Equal("Alice", request.Query["name"].ToString());
        Assert.Equal("30", request.Query["age"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void ContentType_should_read_from_headers()
    {
        var feature = ServerTestContext.Request()
            .Post("/test")
            .Header("Content-Type", "application/json")
            .BuildRequestFeature();
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(feature);
        var request = new TurboHttpRequest(features);

        Assert.Contains("application/json", request.ContentType);
    }

    [Fact(Timeout = 5000)]
    public void Host_should_parse_from_host_header()
    {
        var feature = ServerTestContext.Request()
            .Get("/test")
            .Host("example.com:8080")
            .Header("Host", "example.com:8080")
            .BuildRequestFeature();
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(feature);
        var request = new TurboHttpRequest(features);

        Assert.Equal("example.com:8080", request.Host);
    }

    private static (TurboHttpRequest Request, IFeatureCollection Features) CreateRequest(
        string method, string path, string scheme = "http")
    {
        var feature = ServerTestContext.Request()
            .Method(method)
            .Path(path)
            .Scheme(scheme)
            .BuildRequestFeature();
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(feature);
        return (new TurboHttpRequest(features), features);
    }
}
