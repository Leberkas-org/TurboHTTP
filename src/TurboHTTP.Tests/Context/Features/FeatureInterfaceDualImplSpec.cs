using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Context.Features;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Context.Features;

public sealed class FeatureInterfaceDualImplSpec
{
    [Fact(Timeout = 5000)]
    public void TurboHttpRequestFeature_should_implement_both_interfaces()
    {
        var feature = new TurboHttpRequestFeature();
        Assert.IsAssignableFrom<IHttpRequestFeature>(feature);
        Assert.IsAssignableFrom<ITurboRequestFeature>(feature);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpResponseFeature_should_implement_both_interfaces()
    {
        var feature = new TurboHttpResponseFeature();
        Assert.IsAssignableFrom<IHttpResponseFeature>(feature);
        Assert.IsAssignableFrom<ITurboResponseFeature>(feature);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpConnectionFeature_should_implement_both_interfaces()
    {
        var info = new TurboConnectionInfo("test-id", null, 0, null, 0);
        var feature = new TurboHttpConnectionFeature(info);
        Assert.IsAssignableFrom<IHttpConnectionFeature>(feature);
        Assert.IsAssignableFrom<ITurboConnectionFeature>(feature);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpRequestBodyDetectionFeature_should_implement_both_interfaces()
    {
        var feature = new TurboHttpRequestBodyDetectionFeature(true);
        Assert.IsAssignableFrom<IHttpRequestBodyDetectionFeature>(feature);
        Assert.IsAssignableFrom<ITurboRequestBodyDetectionFeature>(feature);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpResponseTrailersFeature_should_implement_both_interfaces()
    {
        var feature = new TurboHttpResponseTrailersFeature();
        Assert.IsAssignableFrom<IHttpResponseTrailersFeature>(feature);
        Assert.IsAssignableFrom<ITurboResponseTrailersFeature>(feature);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpResetFeature_should_implement_both_interfaces()
    {
        var feature = new TurboHttpResetFeature(_ => { });
        Assert.IsAssignableFrom<IHttpResetFeature>(feature);
        Assert.IsAssignableFrom<ITurboResetFeature>(feature);
    }

    [Fact(Timeout = 5000)]
    public void Request_feature_should_share_state_across_interfaces()
    {
        var feature = new TurboHttpRequestFeature();
        ((IHttpRequestFeature)feature).Method = "POST";
        Assert.Equal("POST", ((ITurboRequestFeature)feature).Method);
    }

    [Fact(Timeout = 5000)]
    public void Response_feature_should_share_state_across_interfaces()
    {
        var feature = new TurboHttpResponseFeature();
        ((IHttpResponseFeature)feature).StatusCode = 404;
        Assert.Equal(404, ((ITurboResponseFeature)feature).StatusCode);
    }
}
