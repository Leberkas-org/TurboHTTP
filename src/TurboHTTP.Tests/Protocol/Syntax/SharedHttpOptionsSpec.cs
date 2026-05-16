using System.Buffers;
using TurboHTTP.Protocol.Syntax;

namespace TurboHTTP.Tests.Protocol.Syntax;

public sealed class SharedHttpOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void Default_should_provide_sensible_values()
    {
        var d = SharedHttpOptions.Default;
        Assert.Equal(64 * 1024L, d.StreamingThreshold);
        Assert.Equal(4 * 1024 * 1024L, d.MaxBufferedBodySize);
        Assert.Null(d.MaxStreamedBodySize);
        Assert.Equal(32 * 1024, d.MaxHeaderBytes);
        Assert.Equal(100, d.MaxHeaderCount);
        Assert.Equal(8 * 1024, d.HeaderLineMaxLength);
        Assert.Equal(8 * 1024, d.RequestLineMaxLength);
        Assert.False(d.AllowObsFold);
        Assert.Same(MemoryPool<byte>.Shared, d.BufferPool);
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_pass_for_default()
    {
        SharedHttpOptions.Default.Validate();
    }

    [Theory(Timeout = 5000)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_should_reject_negative_StreamingThreshold(long bad)
    {
        var opts = SharedHttpOptions.Default with { StreamingThreshold = bad };
        var ex = Assert.Throws<ArgumentException>(opts.Validate);
        Assert.Contains("StreamingThreshold", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_when_MaxBufferedBodySize_below_StreamingThreshold()
    {
        var opts = SharedHttpOptions.Default with
        {
            StreamingThreshold = 100,
            MaxBufferedBodySize = 50,
        };
        var ex = Assert.Throws<ArgumentException>(opts.Validate);
        Assert.Contains("MaxBufferedBodySize", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_when_MaxHeaderCount_zero()
    {
        var opts = SharedHttpOptions.Default with { MaxHeaderCount = 0 };
        Assert.Throws<ArgumentException>(opts.Validate);
    }

    [Fact(Timeout = 5000)]
    public void With_should_create_modified_copy_without_mutation()
    {
        var d = SharedHttpOptions.Default;
        var modified = d with { StreamingThreshold = 1024 };
        Assert.Equal(64 * 1024L, d.StreamingThreshold);
        Assert.Equal(1024, modified.StreamingThreshold);
    }
}