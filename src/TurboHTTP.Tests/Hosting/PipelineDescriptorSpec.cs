using TurboHTTP.Streams;

namespace TurboHTTP.Tests.Hosting;

public sealed class PipelineDescriptorSpec
{
    [Fact(Timeout = 5000)]
    public void PipelineDescriptor_should_have_automatic_decompression_true()
    {
        Assert.True(PipelineDescriptor.Empty.AutomaticDecompression);
    }

    [Fact(Timeout = 5000)]
    public void PipelineDescriptor_should_have_null_policies_and_empty_handlers()
    {
        var empty = PipelineDescriptor.Empty;

        Assert.Null(empty.RedirectPolicy);
        Assert.Null(empty.RetryPolicy);
        Assert.Null(empty.CookieJar);
        Assert.Null(empty.CacheStore);
        Assert.Null(empty.CachePolicy);
        Assert.Empty(empty.Handlers);
    }

    [Fact(Timeout = 5000)]
    public void PipelineDescriptor_should_default_automatic_decompression_to_true()
    {
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null,
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: []);

        Assert.True(descriptor.AutomaticDecompression);
    }

    [Fact(Timeout = 5000)]
    public void PipelineDescriptor_should_allow_automatic_decompression_to_be_set_to_false()
    {
        var descriptor = new PipelineDescriptor(
            RedirectPolicy: null,
            RetryPolicy: null,
            Expect100Policy: null,
            CompressionPolicy: null,
            CookieJar: null,
            CacheStore: null,
            CachePolicy: null,
            Handlers: [],
            AutomaticDecompression: false);

        Assert.False(descriptor.AutomaticDecompression);
    }

    [Fact(Timeout = 5000)]
    public void PipelineDescriptor_should_change_automatic_decompression_with_expression()
    {
        var original = PipelineDescriptor.Empty;
        var modified = original with { AutomaticDecompression = false };

        Assert.True(original.AutomaticDecompression);
        Assert.False(modified.AutomaticDecompression);
    }

    [Fact(Timeout = 5000)]
    public void PipelineDescriptor_should_include_automatic_decompression_in_equality()
    {
        var a = PipelineDescriptor.Empty with { AutomaticDecompression = true };
        var b = PipelineDescriptor.Empty with { AutomaticDecompression = false };

        Assert.NotEqual(a, b);
    }
}
