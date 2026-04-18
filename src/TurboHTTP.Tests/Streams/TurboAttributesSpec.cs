using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Streams;

/// <summary>
/// Tests for TurboAttributes custom Akka.Streams attributes.
/// </summary>
/// <remarks>
/// Types under test: <see cref="TurboAttributes.MemoryBuffer"/>, <see cref="TurboAttributes.SubstreamQueueSize"/>.
/// Akka.Streams: Attributes provide configuration for GraphStage behavior.
/// </remarks>
public sealed class TurboAttributesSpec
{
    [Fact]
    public void MemoryBuffer_should_store_initial_and_max_values()
    {
        var attr = new TurboAttributes.MemoryBuffer(4096, 65536);

        Assert.Equal(4096, attr.Initial);
        Assert.Equal(65536, attr.Max);
    }

    [Fact]
    public void MemoryBuffer_should_implement_equality_correctly()
    {
        var attr1 = new TurboAttributes.MemoryBuffer(4096, 65536);
        var attr2 = new TurboAttributes.MemoryBuffer(4096, 65536);
        var attr3 = new TurboAttributes.MemoryBuffer(8192, 65536);

        Assert.Equal(attr1, attr2);
        Assert.NotEqual(attr1, attr3);
    }

    [Fact]
    public void MemoryBuffer_should_handle_equality_with_null()
    {
        var attr = new TurboAttributes.MemoryBuffer(4096, 65536);
        TurboAttributes.MemoryBuffer? nullAttr = null;

        Assert.NotEqual(attr, nullAttr);
        Assert.False(attr.Equals(null));
    }

    [Fact]
    public void MemoryBuffer_should_implement_equality_with_object()
    {
        var attr1 = new TurboAttributes.MemoryBuffer(4096, 65536);
        var attr2 = new TurboAttributes.MemoryBuffer(4096, 65536);
        object obj = attr2;

        Assert.True(attr1.Equals(obj));
    }

    [Fact]
    public void MemoryBuffer_should_handle_equality_with_incompatible_object_type()
    {
        var attr = new TurboAttributes.MemoryBuffer(4096, 65536);
        object incompatible = "not a MemoryBuffer";

        Assert.False(attr.Equals(incompatible));
    }

    [Fact]
    public void MemoryBuffer_should_generate_same_hash_code_for_equal_instances()
    {
        var attr1 = new TurboAttributes.MemoryBuffer(4096, 65536);
        var attr2 = new TurboAttributes.MemoryBuffer(4096, 65536);

        Assert.Equal(attr1.GetHashCode(), attr2.GetHashCode());
    }

    [Fact]
    public void MemoryBuffer_should_generate_different_hash_codes_for_different_values()
    {
        var attr1 = new TurboAttributes.MemoryBuffer(4096, 65536);
        var attr2 = new TurboAttributes.MemoryBuffer(8192, 65536);

        // Not guaranteed, but very likely
        Assert.NotEqual(attr1.GetHashCode(), attr2.GetHashCode());
    }

    [Fact]
    public void MemoryBuffer_should_have_descriptive_tostring()
    {
        var attr = new TurboAttributes.MemoryBuffer(4096, 65536);
        var str = attr.ToString();

        Assert.Contains("MemoryBuffer", str);
        Assert.Contains("4096", str);
        Assert.Contains("65536", str);
    }

    [Fact]
    public void MemoryBuffer_should_handle_reference_equality()
    {
        var attr1 = new TurboAttributes.MemoryBuffer(4096, 65536);

        Assert.Equal(attr1, attr1);
        Assert.True(attr1.Equals(attr1));
    }

    [Fact]
    public void SubstreamQueueSize_should_store_size_value()
    {
        var attr = new TurboAttributes.SubstreamQueueSize(128);

        Assert.Equal(128, attr.Size);
    }

    [Fact]
    public void SubstreamQueueSize_should_implement_equality_correctly()
    {
        var attr1 = new TurboAttributes.SubstreamQueueSize(128);
        var attr2 = new TurboAttributes.SubstreamQueueSize(128);
        var attr3 = new TurboAttributes.SubstreamQueueSize(256);

        Assert.Equal(attr1, attr2);
        Assert.NotEqual(attr1, attr3);
    }

    [Fact]
    public void SubstreamQueueSize_should_handle_equality_with_null()
    {
        var attr = new TurboAttributes.SubstreamQueueSize(128);
        TurboAttributes.SubstreamQueueSize? nullAttr = null;

        Assert.NotEqual(attr, nullAttr);
        Assert.False(attr.Equals(null));
    }

    [Fact]
    public void SubstreamQueueSize_should_implement_equality_with_object()
    {
        var attr1 = new TurboAttributes.SubstreamQueueSize(128);
        var attr2 = new TurboAttributes.SubstreamQueueSize(128);
        object obj = attr2;

        Assert.True(attr1.Equals(obj));
    }

    [Fact]
    public void SubstreamQueueSize_should_handle_equality_with_incompatible_type()
    {
        var attr = new TurboAttributes.SubstreamQueueSize(128);
        object incompatible = 128;

        Assert.False(attr.Equals(incompatible));
    }

    [Fact]
    public void SubstreamQueueSize_should_generate_same_hash_code_for_equal_instances()
    {
        var attr1 = new TurboAttributes.SubstreamQueueSize(128);
        var attr2 = new TurboAttributes.SubstreamQueueSize(128);

        Assert.Equal(attr1.GetHashCode(), attr2.GetHashCode());
    }

    [Fact]
    public void SubstreamQueueSize_should_generate_different_hash_codes_for_different_values()
    {
        var attr1 = new TurboAttributes.SubstreamQueueSize(128);
        var attr2 = new TurboAttributes.SubstreamQueueSize(256);

        // Not guaranteed, but very likely for different values
        Assert.NotEqual(attr1.GetHashCode(), attr2.GetHashCode());
    }

    [Fact]
    public void SubstreamQueueSize_should_have_descriptive_tostring()
    {
        var attr = new TurboAttributes.SubstreamQueueSize(128);
        var str = attr.ToString();

        Assert.Contains("SubstreamQueueSize", str);
        Assert.Contains("128", str);
    }

    [Fact]
    public void SubstreamQueueSize_should_handle_reference_equality()
    {
        var attr1 = new TurboAttributes.SubstreamQueueSize(128);

        Assert.Equal(attr1, attr1);
        Assert.True(attr1.Equals(attr1));
    }

    [Fact]
    public void SubstreamQueueSize_should_handle_zero_size()
    {
        var attr = new TurboAttributes.SubstreamQueueSize(0);

        Assert.Equal(0, attr.Size);
    }

    [Fact]
    public void SubstreamQueueSize_should_handle_large_size()
    {
        var attr = new TurboAttributes.SubstreamQueueSize(1_000_000);

        Assert.Equal(1_000_000, attr.Size);
    }

    [Fact]
    public void MemoryBuffer_should_handle_zero_values()
    {
        var attr = new TurboAttributes.MemoryBuffer(0, 0);

        Assert.Equal(0, attr.Initial);
        Assert.Equal(0, attr.Max);
    }

    [Fact]
    public void MemoryBuffer_should_handle_large_values()
    {
        var attr = new TurboAttributes.MemoryBuffer(1024 * 1024, 512 * 1024 * 1024);

        Assert.Equal(1024 * 1024, attr.Initial);
        Assert.Equal(512 * 1024 * 1024, attr.Max);
    }
}
