using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Tests.Server.Context;

public sealed class TurboHttpRequestSpec
{
    [Fact(Timeout = 5000)]
    public void Method_should_delegate_to_feature()
    {
        var (request, _) = CreateRequest("POST", "http://localhost/test");
        Assert.Equal("POST", request.Method);
    }

    [Fact(Timeout = 5000)]
    public void Path_should_delegate_to_feature()
    {
        var (request, _) = CreateRequest("GET", "http://localhost/api/users");
        Assert.Equal("/api/users", request.Path.Value);
    }

    [Fact(Timeout = 5000)]
    public void QueryString_should_delegate_to_feature()
    {
        var (request, _) = CreateRequest("GET", "http://localhost/test?page=1");
        Assert.Equal("?page=1", request.QueryString.Value);
    }

    [Fact(Timeout = 5000)]
    public void Scheme_should_delegate_to_feature()
    {
        var (request, _) = CreateRequest("GET", "https://localhost/test");
        Assert.Equal("https", request.Scheme);
    }

    [Fact(Timeout = 5000)]
    public void Protocol_should_delegate_to_feature()
    {
        var (request, _) = CreateRequest("GET", "http://localhost/test");
        Assert.Equal("HTTP/1.1", request.Protocol);
    }

    [Fact(Timeout = 5000)]
    public void Headers_should_expose_request_headers()
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        msg.Headers.Add("X-Custom", "val");
        var features = CreateFeatures(msg);
        var request = new TurboHttpRequest(features);

        Assert.Equal("val", request.Headers["X-Custom"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Query_should_parse_query_string()
    {
        var (request, _) = CreateRequest("GET", "http://localhost/test?name=Alice&age=30");
        Assert.Equal("Alice", request.Query["name"].ToString());
        Assert.Equal("30", request.Query["age"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void ContentType_should_read_from_headers()
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "http://localhost/test")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        var features = CreateFeatures(msg);
        var request = new TurboHttpRequest(features);

        Assert.Contains("application/json", request.ContentType);
    }

    [Fact(Timeout = 5000)]
    public void Host_should_parse_from_host_header()
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/test");
        msg.Headers.Host = "example.com:8080";
        var features = CreateFeatures(msg);
        var request = new TurboHttpRequest(features);

        Assert.Equal("example.com:8080", request.Host.Value);
    }

    private static (TurboHttpRequest Request, IFeatureCollection Features) CreateRequest(string method, string url)
    {
        var msg = new HttpRequestMessage(new HttpMethod(method), url);
        var features = CreateFeatures(msg);
        return (new TurboHttpRequest(features), features);
    }

    private static FeatureCollection CreateFeatures(HttpRequestMessage msg)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature(msg, Source.Empty<ReadOnlyMemory<byte>>()));
        return features;
    }
}
