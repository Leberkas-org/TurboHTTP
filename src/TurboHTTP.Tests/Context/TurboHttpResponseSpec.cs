using System.Net;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context;
using TurboHTTP.Context.Features;

namespace TurboHTTP.Tests.Context;

public sealed class TurboHttpResponseSpec
{
    [Fact(Timeout = 5000)]
    public void StatusCode_should_delegate_to_feature()
    {
        var (response, _) = CreateResponse(HttpStatusCode.NotFound);
        Assert.Equal(404, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void StatusCode_set_should_update_feature()
    {
        var (response, _) = CreateResponse(HttpStatusCode.OK);
        response.StatusCode = 201;
        Assert.Equal(201, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void Headers_should_expose_response_headers()
    {
        var features = CreateFeatures();
        var feature = features.Get<IHttpResponseFeature>()!;
        feature.Headers["X-Custom"] = "val";
        var response = new TurboHttpResponse(features);

        Assert.Equal("val", response.Headers["X-Custom"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void ContentType_should_set_header()
    {
        var (response, _) = CreateResponse(HttpStatusCode.OK);
        response.ContentType = "text/plain";
        Assert.Equal("text/plain", response.ContentType);
    }

    [Fact(Timeout = 5000)]
    public void HasStarted_should_be_false_initially()
    {
        var (response, _) = CreateResponse(HttpStatusCode.OK);
        Assert.False(response.HasStarted);
    }

    [Fact(Timeout = 5000)]
    public void Body_should_return_writable_stream()
    {
        var (response, features) = CreateResponse(HttpStatusCode.OK);
        features.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());
        Assert.NotNull(response.Body);
    }

    [Fact(Timeout = 5000)]
    public void Redirect_should_set_status_and_location()
    {
        var (response, _) = CreateResponse(HttpStatusCode.OK);
        response.Redirect("/new-location");
        Assert.Equal(302, response.StatusCode);
        Assert.Equal("/new-location", response.Headers["Location"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Redirect_permanent_should_set_301()
    {
        var (response, _) = CreateResponse(HttpStatusCode.OK);
        response.Redirect("/new-location", permanent: true);
        Assert.Equal(301, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void Redirect_should_accept_absolute_https_url()
    {
        var (response, _) = CreateResponse(HttpStatusCode.OK);
        response.Redirect("https://example.com/path");
        Assert.Equal(302, response.StatusCode);
        Assert.Equal("https://example.com/path", response.Headers["Location"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Redirect_should_reject_non_http_scheme()
    {
        var (response, _) = CreateResponse(HttpStatusCode.OK);
        Assert.Throws<ArgumentException>(() => response.Redirect("javascript:alert(1)"));
    }

    [Fact(Timeout = 5000)]
    public void Redirect_should_reject_null_location()
    {
        var (response, _) = CreateResponse(HttpStatusCode.OK);
        Assert.Throws<ArgumentNullException>(() => response.Redirect(null!));
    }

    private static (TurboHttpResponse Response, FeatureCollection Features) CreateResponse(HttpStatusCode status)
    {
        var features = CreateFeatures((int)status);
        return (new TurboHttpResponse(features), features);
    }

    private static FeatureCollection CreateFeatures(int statusCode = 200)
    {
        var feature = new TurboHttpResponseFeature
        {
            StatusCode = statusCode
        };
        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(feature);
        return features;
    }
}
