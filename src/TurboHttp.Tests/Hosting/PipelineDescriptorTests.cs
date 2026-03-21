using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Streams;

namespace TurboHttp.Tests.Hosting;

public sealed class PipelineDescriptorTests
{
    [Fact(DisplayName = "PipelineDescriptor.Empty has AutomaticDecompression true")]
    public void Empty_HasAutomaticDecompressionTrue()
    {
        Assert.True(PipelineDescriptor.Empty.AutomaticDecompression);
    }

    [Fact(DisplayName = "PipelineDescriptor.Empty has null policies and empty middlewares")]
    public void Empty_HasNullPoliciesAndEmptyHandlers()
    {
        var empty = PipelineDescriptor.Empty;

        Assert.Null(empty.RedirectPolicy);
        Assert.Null(empty.RetryPolicy);
        Assert.Null(empty.CookieJar);
        Assert.Null(empty.CacheStore);
        Assert.Null(empty.CachePolicy);
        Assert.Empty(empty.Handlers);
    }

    [Fact(DisplayName = "AutomaticDecompression defaults to true when not specified")]
    public void AutomaticDecompression_DefaultsToTrue()
    {
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null,
            RetryPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);

        Assert.True(descriptor.AutomaticDecompression);
    }

    [Fact(DisplayName = "AutomaticDecompression can be set to false")]
    public void AutomaticDecompression_CanBeSetToFalse()
    {
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null,
            RetryPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: [],
            AutomaticDecompression: false);

        Assert.False(descriptor.AutomaticDecompression);
    }

    [Fact(DisplayName = "PipelineDescriptor with expression creates copy with changed AutomaticDecompression")]
    public void WithExpression_ChangesAutomaticDecompression()
    {
        var original = PipelineDescriptor.Empty;
        var modified = original with { AutomaticDecompression = false };

        Assert.True(original.AutomaticDecompression);
        Assert.False(modified.AutomaticDecompression);
    }

    [Fact(DisplayName = "PipelineDescriptor equality includes AutomaticDecompression")]
    public void Equality_IncludesAutomaticDecompression()
    {
        var a = PipelineDescriptor.Empty with { AutomaticDecompression = true };
        var b = PipelineDescriptor.Empty with { AutomaticDecompression = false };

        Assert.NotEqual(a, b);
    }
}
