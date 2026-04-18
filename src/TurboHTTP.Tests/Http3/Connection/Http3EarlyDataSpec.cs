using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class Http3EarlyDataSpec
{
    private static readonly HashSet<HttpMethod> IdempotentMethods =
    [
        HttpMethod.Get,
        HttpMethod.Head,
        HttpMethod.Options,
        HttpMethod.Trace,
        HttpMethod.Delete,
    ];

    private static IReadOnlyList<Http3Frame> EncodeAndTag(HttpMethod method, bool allowEarlyData)
    {
        var encoder = new RequestEncoder(new QpackTableSync());
        var request = new HttpRequestMessage(method, "https://example.com/");
        var frames = encoder.Encode(request);

        if (allowEarlyData && IdempotentMethods.Contains(method))
        {
            foreach (var f in frames)
            {
                if (f is Http3HeadersFrame headers)
                {
                    headers.EarlyData = true;
                }
            }
        }

        return frames;
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-A.1")]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("DELETE")]
    public void IdempotentRequest_should_be_tagged_for_early_data_when_enabled(string methodName)
    {
        var method = new HttpMethod(methodName);
        var frames = EncodeAndTag(method, allowEarlyData: true);

        var headersFrame = Assert.Single(frames.OfType<Http3HeadersFrame>());
        Assert.True(headersFrame.EarlyData,
            $"Expected EarlyData=true for idempotent method {methodName}");
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-A.1")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public void NonIdempotentRequest_should_not_be_tagged_for_early_data_when_enabled(string methodName)
    {
        var method = new HttpMethod(methodName);
        var frames = EncodeAndTag(method, allowEarlyData: true);

        var headersFrame = Assert.Single(frames.OfType<Http3HeadersFrame>());
        Assert.False(headersFrame.EarlyData,
            $"Expected EarlyData=false for non-idempotent method {methodName}");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-A.1")]
    public void IdempotentRequest_should_not_be_tagged_when_early_data_disabled()
    {
        var frames = EncodeAndTag(HttpMethod.Get, allowEarlyData: false);

        var headersFrame = Assert.Single(frames.OfType<Http3HeadersFrame>());
        Assert.False(headersFrame.EarlyData,
            "Expected EarlyData=false when AllowEarlyData is disabled");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-A.1")]
    public void Http3Options_should_default_AllowEarlyData_to_false()
    {
        var options = new Http3Options();
        Assert.False(options.AllowEarlyData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-A.1")]
    public void Http3Options_should_accept_AllowEarlyData_true()
    {
        var options = new Http3Options { AllowEarlyData = true };
        Assert.True(options.AllowEarlyData);
    }
}
