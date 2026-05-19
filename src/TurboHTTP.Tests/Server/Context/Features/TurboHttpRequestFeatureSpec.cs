using Akka.Streams.Dsl;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server.Context.Features;

public sealed class TurboHttpRequestFeatureSpec
{
    [Fact(Timeout = 5000)]
    public void Method_should_delegate_to_request_message()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/test");
        var feature = CreateFeature(request);
        Assert.Equal("POST", feature.Method);
    }

    [Fact(Timeout = 5000)]
    public void Method_set_should_update_request_message()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var feature = CreateFeature(request);
        feature.Method = "PUT";
        Assert.Equal("PUT", request.Method.Method);
    }

    [Fact(Timeout = 5000)]
    public void Path_should_delegate_to_request_uri()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/users");
        var feature = CreateFeature(request);
        Assert.Equal("/api/users", feature.Path);
    }

    [Fact(Timeout = 5000)]
    public void QueryString_should_delegate_to_request_uri()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test?page=1&size=10");
        var feature = CreateFeature(request);
        Assert.Equal("?page=1&size=10", feature.QueryString);
    }

    [Fact(Timeout = 5000)]
    public void Scheme_should_delegate_to_request_uri()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/test");
        var feature = CreateFeature(request);
        Assert.Equal("https", feature.Scheme);
    }

    [Fact(Timeout = 5000)]
    public void Protocol_should_map_version()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test") { Version = new Version(2, 0) };
        var feature = CreateFeature(request);
        Assert.Equal("HTTP/2", feature.Protocol);
    }

    [Fact(Timeout = 5000)]
    public void Headers_should_return_IHeaderDictionary()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        request.Headers.Add("X-Custom", "value");
        var feature = CreateFeature(request);
        Assert.Equal("value", feature.Headers["X-Custom"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void RawTarget_should_return_original_uri_string()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test?a=1");
        var feature = CreateFeature(request);
        Assert.Contains("/test", feature.RawTarget);
    }

    [Fact(Timeout = 5000)]
    public void BodySource_should_expose_original_akka_source()
    {
        var source = Source.Single(new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }));
        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/test");
        var feature = new TurboHttpRequestFeature(request, source);
        var bodyFeature = (ITurboRequestBodyFeature)feature;
        Assert.Same(source, bodyFeature.BodySource);
    }

    private static TurboHttpRequestFeature CreateFeature(HttpRequestMessage request)
    {
        return new TurboHttpRequestFeature(request, Source.Empty<ReadOnlyMemory<byte>>());
    }
}
